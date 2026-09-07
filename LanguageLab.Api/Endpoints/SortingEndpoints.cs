using LanguageLab.Application.Services;

namespace LanguageLab.Api.Endpoints;

public sealed record MarkRequest(long WordPairId, SortStatus Status);

public static class SortingEndpoints
{
    public static void MapSortingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sorting");

        group.MapGet("/queue", async (
            long dictionaryId,
            string? chapterIds,
            int? take,
            WordSortingService sorting,
            ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            var chapters = ParseChapterIds(chapterIds);

            var queue = await sorting.GetQueueAsync(
                userId, dictionaryId, chapters, take ?? WordSortingService.DefaultTake);

            return Results.Ok(queue);
        });

        group.MapPost("/mark", async (
            MarkRequest request, WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            await sorting.MarkAsync(userId, request.WordPairId, request.Status, DateTime.UtcNow);
            return Results.NoContent();
        });

        group.MapPost("/undo", async (WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            var undone = await sorting.UndoAsync(userId);

            return undone == null ? Results.NoContent() : Results.Ok(undone);
        });

        group.MapGet("/recent", async (
            int? take, WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            return Results.Ok(await sorting.GetRecentAsync(userId, take ?? 10));
        });
    }

    /// <summary>«1,2,3» → [1, 2, 3]. Порожній або кривий рядок означає «вся книжка».</summary>
    private static List<long>? ParseChapterIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ids = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        return ids.Count == 0 ? null : ids;
    }
}
