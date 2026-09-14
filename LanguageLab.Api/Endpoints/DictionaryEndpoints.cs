using LanguageLab.Application.Services;
using LanguageLab.Infrastructure.Database;
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
    bool IsPublic);

public sealed record VisibilityRequest(bool IsPublic);

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
            var entries = (request.Words ?? [])
                .Select(w => new BulkWordEntry(w.Word ?? string.Empty, w.Translation ?? string.Empty))
                .ToList();

            var outcomes = await personal.AddManyAsync(await currentUser.GetIdAsync(), entries, DateTime.UtcNow);

            return Results.Ok(outcomes);
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
            ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();
            var now = DateTime.UtcNow;

            var dictionary = await access.Visible(userId, role)
                .Where(d => d.Id == id)
                .Select(d => new { d.Id, d.Name, d.WordsCount, d.IsPublic })
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
                dictionary.IsPublic));
        });

        group.MapPost("/import", async (
            ImportRequest request, BookImportService import, ICurrentUserContext currentUser) =>
        {
            var result = await import.ImportAsync(
                request, currentUser.Require().Id, request.IsPublic ?? true);

            return Results.Ok(result);
        }).RequireAuthorization("Admin");

        group.MapDelete("/{id:long}", async (long id, ApplicationDbContext db) =>
        {
            // A personal dictionary is invisible to everyone but its owner, even an admin —
            // same rule as the read paths (DictionaryAccessService.Visible). Excluding it here
            // makes its id fall through to the ordinary NotFound path, not a 403.
            var dictionary = await db.Dictionaries.FirstOrDefaultAsync(d => d.Id == id && !d.IsPersonal);

            if (dictionary == null)
            {
                return Results.NotFound();
            }

            // Каскади знесуть Chapters, ChapterWords і DictionaryWords.
            // WordPair і полиці юзера лишаються — вони глобальні.
            db.Dictionaries.Remove(dictionary);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization("Admin");

        group.MapPatch("/{id:long}", async (long id, VisibilityRequest request, ApplicationDbContext db) =>
        {
            // Same personal-dictionary exclusion as the DELETE handler above.
            var dictionary = await db.Dictionaries.FirstOrDefaultAsync(d => d.Id == id && !d.IsPersonal);

            if (dictionary == null)
            {
                return Results.NotFound();
            }

            dictionary.IsPublic = request.IsPublic;
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }
}
