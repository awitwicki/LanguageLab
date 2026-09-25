using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

public sealed record DictionaryListItem(long Id, string Name, int WordsCount, bool HasChapters, int SortedCount, bool IsPersonal);

public sealed record AddPersonalWordRequest(string? Word, string? Translation);

public sealed record BulkWordEntryRequest(string? Word, string? Translation);

public sealed record AddPersonalWordsRequest(IReadOnlyList<BulkWordEntryRequest>? Words);

/// <summary>A refusal written for the user; the client shows Message instead of the status code.</summary>
public sealed record DictionaryError(string Message);

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
    PublicationStatus Status);

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
            ICurrentUserContext currentUser) =>
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

            var items = new List<DictionaryListItem>(dictionaries.Count);

            foreach (var d in dictionaries)
            {
                var queue = await sorting.GetQueueAsync(userId, d.Id, chapterIds: null, take: 1);
                items.Add(new DictionaryListItem(d.Id, d.Name, d.WordsCount, d.HasChapters, queue.Sorted, d.IsPersonal));
            }

            return Results.Ok(items);
        });

        // The caller's own word list — its own screen, so its own shape: no chapters, no
        // sorting, the words themselves instead of a top-frequency list.
        group.MapGet("/personal", async (PersonalDictionaryService personal, ICurrentUser currentUser) =>
            Results.Ok(await personal.GetAsync(await currentUser.GetIdAsync(), DateTime.UtcNow)));

        group.MapPost("/personal/words", async (
            AddPersonalWordRequest request, PersonalDictionaryService personal, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            PersonalWord? added;

            try
            {
                added = await personal.AddAsync(
                    userId, request.Word ?? string.Empty, request.Translation ?? string.Empty, DateTime.UtcNow);
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
            AddPersonalWordsRequest request, PersonalDictionaryService personal, ICurrentUser currentUser) =>
        {
            try
            {
                var entries = (request.Words ?? [])
                    .Select(w => new BulkWordEntry(w.Word ?? string.Empty, w.Translation ?? string.Empty))
                    .ToList();

                var outcomes = await personal.AddManyAsync(await currentUser.GetIdAsync(), entries, DateTime.UtcNow);

                return Results.Ok(outcomes);
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(1L * 1024 * 1024))
          .RequireRateLimiting(UserRateLimits.BulkWords);

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
            ICurrentUserContext currentUser) =>
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

            var whole = await sorting.GetQueueAsync(userId, id, chapterIds: null, take: 1);
            var topWords = await stats.GetTopWordsAsync(id, userId);

            // "To learn" = translated, on the "don't know" shelf, never trained — what a new batch takes.
            var learnable = await selection.CountLearnableAsync(userId, id);
            var due = await selection.CountDueAsync(userId, now, id);

            // The book's own box breakdown; the chapters' come with their rows.
            var learning = await learningProgress.GetAsync(userId, id);
            var chapterViews = await chapterStats.GetChapterViewsAsync(userId, id, now);

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
                dictionary.PublicationStatus));
        });

        group.MapPost("/import", async (
            ImportRequest request, BookImportService import, ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            try
            {
                var result = await import.ImportAsync(
                    request, userId, BookImportService.StatusFor(role, request.RequestPublication));

                return Results.Ok(result);
            }
            catch (ArgumentException e)
            {
                // An empty name or a book with no words is the importer's mistake, and the
                // message says which: a bare 500 would leave them staring at a status code.
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        })
          // A 50 000-word book is a few megabytes of JSON; the global 64 MB is headroom this
          // endpoint does not need, and it is the only one a stranger can make large.
          .WithMetadata(new RequestSizeLimitAttribute(16L * 1024 * 1024))
          .RequireRateLimiting(UserRateLimits.Import);

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
