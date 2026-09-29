using System.Globalization;
using LanguageLab.Api.Auth;
using LanguageLab.Application.Books;
using LanguageLab.Application.Import;
using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Queue;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

public sealed record DictionaryListItem(long Id, string Name, int WordsCount, bool HasChapters, int SortedCount, bool IsPersonal);

public sealed record AddPersonalWordRequest(string? Word, string? Translation);

public sealed record BulkWordEntryRequest(string? Word, string? Translation);

public sealed record AddPersonalWordsRequest(IReadOnlyList<BulkWordEntryRequest>? Words);

public sealed record UpdatePersonalWordRequest(string? Translation);

/// <summary>
/// A refusal written for the user; the client shows Message instead of the status code.
/// Error is a machine code for refusals the SPA words itself (C4): "invalid_book",
/// "encrypted_book" or "not_english"; null for plain validation messages.
/// </summary>
public sealed record DictionaryError(string Message, string? Error = null);

public sealed record DictionaryDetail(
    long Id,
    string Name,
    int WordsCount,
    int SortedCount,
    int LearnableCount,
    int DueCount,
    LearningProgress Learning,
    IReadOnlyList<ChapterView> Chapters,
    IReadOnlyList<TopWord> TopWords,
    PublicationStatus Status,
    TranslationProgress? Translation);

public sealed record StatusRequest(PublicationStatus Status);

public static class DictionaryEndpoints
{
    public static void MapDictionaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dictionaries").RequireAuthorization();

        group.MapGet("/", async (
            WordSortingService sorting,
            DictionaryAccessService access,
            PersonalDictionaryService personal,
            ICurrentUserContext currentUser,
            ICurrentLanguage language) =>
        {
            var (userId, role) = currentUser.Require();

            // A GET that creates, deliberately: idempotent and invisible, and it means the SPA
            // always finds "My words" in this list without a bootstrap step — creating it at
            // login would miss everyone already signed in on a 30-day cookie.
            await personal.GetOrCreateAsync(userId);

            var dictionaries = await access.Visible(userId, role)
                .OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name, d.WordsCount, HasChapters = d.Chapters.Any(), d.IsPersonal })
                .ToListAsync();

            var code = language.Require().Code;
            var items = new List<DictionaryListItem>(dictionaries.Count);

            foreach (var d in dictionaries)
            {
                var queue = await sorting.GetQueueAsync(userId, code, d.Id, chapterIds: null, take: 1);
                items.Add(new DictionaryListItem(d.Id, d.Name, d.WordsCount, d.HasChapters, queue.Sorted, d.IsPersonal));
            }

            return Results.Ok(items);
        });

        // The caller's own word list — its own screen, so its own shape: no chapters, no
        // sorting, the words themselves instead of a top-frequency list.
        group.MapGet("/personal", async (
            PersonalDictionaryService personal, ICurrentUser currentUser, ICurrentLanguage language) =>
            Results.Ok(await personal.GetAsync(await currentUser.GetIdAsync(), language.Require().Code, DateTime.UtcNow)));

        group.MapPost("/personal/words", async (
            AddPersonalWordRequest request, PersonalDictionaryService personal, ICurrentUser currentUser, ICurrentLanguage language) =>
        {
            var userId = await currentUser.GetIdAsync();
            PersonalWord? added;

            try
            {
                added = await personal.AddAsync(
                    userId, language.Require().Code, request.Word ?? string.Empty, request.Translation ?? string.Empty, DateTime.UtcNow);
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }

            return added == null
                ? Results.Json(new DictionaryError("Already in your dictionary."), statusCode: StatusCodes.Status409Conflict)
                : Results.Created($"/api/dictionaries/personal/words/{added.WordPairId}", added);
        });

        group.MapPost("/personal/words/import", async (
            AddPersonalWordsRequest request, PersonalDictionaryService personal, ICurrentUser currentUser, ICurrentLanguage language) =>
        {
            try
            {
                var entries = (request.Words ?? [])
                    .Select(w => new BulkWordEntry(w.Word ?? string.Empty, w.Translation ?? string.Empty))
                    .ToList();

                var outcomes = await personal.AddManyAsync(
                    await currentUser.GetIdAsync(), language.Require().Code, entries, DateTime.UtcNow);

                return Results.Ok(outcomes);
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(1L * 1024 * 1024))
          .RequireRateLimiting(UserRateLimits.BulkWords);

        // The translation only: the word itself is what the (Word, OwnerId) index is built on,
        // so changing it would be an add and a remove, not an edit.
        group.MapPut("/personal/words/{wordPairId:long}", async (
            long wordPairId,
            UpdatePersonalWordRequest request,
            PersonalDictionaryService personal,
            ICurrentUser currentUser,
            ICurrentLanguage language) =>
        {
            PersonalWord? updated;

            try
            {
                updated = await personal.UpdateTranslationAsync(
                    await currentUser.GetIdAsync(), language.Require().Code, wordPairId, request.Translation ?? string.Empty);
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }

            return updated == null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/personal/words/{wordPairId:long}", async (
            long wordPairId, PersonalDictionaryService personal, ICurrentUser currentUser) =>
            await personal.RemoveAsync(await currentUser.GetIdAsync(), wordPairId)
                ? Results.NoContent()
                : Results.NotFound());

        group.MapGet("/{id:long}", async (
            long id,
            WordSortingService sorting,
            DictionaryStatsService stats,
            WordSelectionService selection,
            LearningProgressService learningProgress,
            ChapterStatsService chapterStats,
            DictionaryAccessService access,
            ITranslationQueue queue,
            TranslationJobProgressReader progress,
            ICurrentUserContext currentUser,
            ICurrentLanguage language,
            CancellationToken cancellationToken) =>
        {
            var (userId, role) = currentUser.Require();
            var now = DateTime.UtcNow;

            var dictionary = await access.Visible(userId, role)
                .Where(d => d.Id == id)
                .Select(d => new { d.Id, d.Name, d.WordsCount, d.PublicationStatus })
                .FirstOrDefaultAsync();

            if (dictionary == null)
            {
                // 404 rather than 403: a private dictionary should not be probeable by id.
                return Results.NotFound();
            }

            // "To learn" = translated into the learner's language, on the "don't know" shelf, never trained — what a new batch takes.
            var learnerLanguage = language.Require();
            var code = learnerLanguage.Code;
            var whole = await sorting.GetQueueAsync(userId, code, id, chapterIds: null, take: 1);
            var topWords = await stats.GetTopWordsAsync(id, userId);

            var learnable = await selection.CountLearnableAsync(userId, code, id);
            var due = await selection.CountDueAsync(userId, code, now, id);

            // The book's own box breakdown; the chapters' come with their rows.
            var learning = await learningProgress.GetAsync(userId, code, id);
            var chapterViews = await chapterStats.GetChapterViewsAsync(userId, code, id, now);

            // Opening the dictionary is one of B2's Q6 triggers: freely re-enqueue (EnqueueAsync
            // is idempotent while a job is pending, and re-arms a finished one) then read back
            // whatever the job now looks like, so a just-re-armed job's fresh Total is what the
            // client sees.
            await queue.EnqueueAsync(id, learnerLanguage, cancellationToken);
            var translation = await progress.GetAsync(id, code);

            return Results.Ok(new DictionaryDetail(
                dictionary.Id,
                dictionary.Name,
                dictionary.WordsCount,
                whole.Sorted,
                learnable,
                due,
                learning,
                chapterViews,
                topWords,
                dictionary.PublicationStatus,
                translation));
        });

        // Read-only, and deliberately does not enqueue: the dictionary screen polls this while a
        // job is running (final review, finding 1). GET /{id} above is the only place that
        // enqueues on open — polling it too turned every 4s tick into a fresh EnqueueAsync, so a
        // job that finished with leftover skipped words (or one whose provider had just failed
        // five times) was continuously re-armed for as long as the tab stayed open.
        group.MapGet("/{id:long}/translation", async (
            long id,
            DictionaryAccessService access,
            TranslationJobProgressReader progress,
            ICurrentUserContext currentUser,
            ICurrentLanguage language) =>
        {
            var (userId, role) = currentUser.Require();

            if (!await access.IsVisibleAsync(id, userId, role))
            {
                return Results.NotFound();
            }

            return Results.Ok(await progress.GetAsync(id, language.Require().Code));
        });

        // Since C3 the import takes the book file itself: the server hashes, parses, tokenizes
        // against the lexicon and imports — the client never sends word lists (the C4 SPA
        // switch-over consumes this contract).
        group.MapPost("/import", async (
            [FromForm] IFormFile? file,
            [FromForm] int? chapterMode,
            BookFileImportService import,
            ICurrentUserContext currentUser,
            ICurrentLanguage language,
            HttpContext httpContext,
            CancellationToken cancellationToken,
            [FromForm] bool requestPublication = false) =>
        {
            var (userId, role) = currentUser.Require();

            if (file is null || file.Length == 0)
            {
                return Results.Json(
                    new DictionaryError("The upload has no book file."),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            byte[] bytes;

            using (var buffer = new MemoryStream((int)file.Length))
            {
                await file.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }

            try
            {
                var result = await import.ImportAsync(
                    bytes, file.FileName, new ChapterMode(chapterMode), requestPublication,
                    userId, role, language.Get(), cancellationToken);

                return Results.Ok(result);
            }
            catch (ImportQuotaExceededException e)
            {
                httpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(e.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status429TooManyRequests);
            }
            catch (BookFormatException e)
            {
                var code = e.Error == BookFormatError.Encrypted ? "encrypted_book" : "invalid_book";

                return Results.Json(new DictionaryError(e.Message, code), statusCode: StatusCodes.Status400BadRequest);
            }
            catch (NotEnglishBookException e)
            {
                return Results.Json(new DictionaryError(e.Message, "not_english"), statusCode: StatusCodes.Status400BadRequest);
            }
            catch (ArgumentException e)
            {
                // An empty name or a book with no words is the importer's mistake, and the
                // message says which: a bare 500 would leave them staring at a status code.
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        })
          // Form binding demands antiforgery or an explicit opt-out; the API's CSRF story is the
          // SameSite session cookie, same as every other endpoint here (docs/auth.md).
          .DisableAntiforgery()
          // A book file is a few megabytes; the global 64 MB is headroom this endpoint does not
          // need, and it is the only one a stranger can make large.
          .WithMetadata(new RequestSizeLimitAttribute(16L * 1024 * 1024))
          // A loose attempts ceiling, independent of ImportQuota's 1-success/day: the middleware
          // rejects before the body is even read, so a spent-quota or repeatedly-wrong-file
          // caller cannot make the server buffer and parse an unbounded number of uploads.
          .RequireRateLimiting(UserRateLimits.ImportAttempts);

        // A personal dictionary is invisible to everyone but its owner, even an admin — same
        // rule as the read paths (DictionaryAccessService.Visible); DictionaryDeletionService
        // never touches one, so its id falls through to the ordinary NotFound path, not a 403.
        group.MapDelete("/{id:long}", async (long id, DictionaryDeletionService deletion) =>
            await deletion.DeleteAsync(id) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AuthPolicies.Admin);

        group.MapPatch("/{id:long}", async (long id, StatusRequest request, DictionaryPublicationService publication) =>
        {
            if (!Enum.IsDefined(request.Status))
            {
                return Results.BadRequest();
            }

            return MapPublication(await publication.SetStatusAsync(id, request.Status));
        }).RequireAuthorization(AuthPolicies.Admin);

        // The owner offers a dictionary for publication and may take the offer back; the
        // decision itself is an admin's — the PATCH handler above, or the moderation queue under
        // /api/admin/dictionaries (AdminEndpoints.cs).
        group.MapPost("/{id:long}/publication", async (
            long id, DictionaryPublicationService publication, ICurrentUser currentUser) =>
            MapPublication(await publication.RequestAsync(await currentUser.GetIdAsync(), id)));

        group.MapDelete("/{id:long}/publication", async (
            long id, DictionaryPublicationService publication, ICurrentUser currentUser) =>
            MapPublication(await publication.WithdrawAsync(await currentUser.GetIdAsync(), id)));
    }

    private static IResult MapPublication(PublicationActionResult result) => result switch
    {
        PublicationActionResult.Ok => Results.NoContent(),
        PublicationActionResult.NotFound => Results.NotFound(),
        PublicationActionResult.WrongState => Results.Json(
            new DictionaryError("This dictionary is not waiting for that."),
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}
