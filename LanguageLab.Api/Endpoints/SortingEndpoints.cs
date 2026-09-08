using LanguageLab.Application.Services;

namespace LanguageLab.Api.Endpoints;

public sealed record MarkRequest(long WordPairId, SortStatus Status);

public static class SortingEndpoints
{
    public static void MapSortingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sorting").RequireAuthorization();

        group.MapGet("/queue", async (
            long dictionaryId,
            string? chapterIds,
            int? take,
            WordSortingService sorting,
            DictionaryAccessService access,
            ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            if (!await access.IsVisibleAsync(dictionaryId, userId, role))
            {
                return Results.NotFound();
            }

            var chapters = QueryParsing.ParseChapterIds(chapterIds);

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
}
