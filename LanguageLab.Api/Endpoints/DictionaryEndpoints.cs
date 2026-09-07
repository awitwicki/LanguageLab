using LanguageLab.Application.Services;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

public sealed record DictionaryListItem(long Id, string Name, int WordsCount, bool HasChapters, int SortedCount);

public sealed record ChapterView(long Id, int Order, string Title, int WordsCount, int SortedCount);

public sealed record DictionaryDetail(
    long Id, string Name, int WordsCount, int SortedCount, IReadOnlyList<ChapterView> Chapters);

public static class DictionaryEndpoints
{
    public static void MapDictionaryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dictionaries");

        group.MapGet("/", async (
            ApplicationDbContext db, WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            var dictionaries = await db.Dictionaries
                .OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name, d.WordsCount, HasChapters = d.Chapters.Any() })
                .ToListAsync();

            var items = new List<DictionaryListItem>(dictionaries.Count);

            foreach (var d in dictionaries)
            {
                var queue = await sorting.GetQueueAsync(userId, d.Id, chapterIds: null, take: 1);
                items.Add(new DictionaryListItem(d.Id, d.Name, d.WordsCount, d.HasChapters, queue.Sorted));
            }

            return Results.Ok(items);
        });

        group.MapGet("/{id:long}", async (
            long id, ApplicationDbContext db, WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            var dictionary = await db.Dictionaries
                .Where(d => d.Id == id)
                .Select(d => new { d.Id, d.Name, d.WordsCount })
                .FirstOrDefaultAsync();

            if (dictionary == null)
            {
                return Results.NotFound();
            }

            var chapters = await db.Chapters
                .Where(c => c.DictionaryId == id)
                .OrderBy(c => c.Order)
                .Select(c => new { c.Id, c.Order, c.Title, c.WordsCount })
                .ToListAsync();

            var progress = (await sorting.GetChapterProgressAsync(userId, id))
                .ToDictionary(p => p.ChapterId);

            var whole = await sorting.GetQueueAsync(userId, id, chapterIds: null, take: 1);

            return Results.Ok(new DictionaryDetail(
                dictionary.Id,
                dictionary.Name,
                dictionary.WordsCount,
                whole.Sorted,
                chapters
                    .Select(c => new ChapterView(
                        c.Id, c.Order, c.Title, c.WordsCount,
                        progress.TryGetValue(c.Id, out var p) ? p.Sorted : 0))
                    .ToList()));
        });

        group.MapPost("/import", async (ImportRequest request, BookImportService import) =>
        {
            var result = await import.ImportAsync(request);
            return Results.Ok(result);
        });

        group.MapDelete("/{id:long}", async (long id, ApplicationDbContext db) =>
        {
            var dictionary = await db.Dictionaries.FirstOrDefaultAsync(d => d.Id == id);

            if (dictionary == null)
            {
                return Results.NotFound();
            }

            // Каскади знесуть Chapters, ChapterWords і DictionaryWords.
            // WordPair і полиці юзера лишаються — вони глобальні.
            db.Dictionaries.Remove(dictionary);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}
