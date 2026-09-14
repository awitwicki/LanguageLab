using LanguageLab.Application.Services;

namespace LanguageLab.Api.Endpoints;

/// <summary>Chapter ids are global, so the star routes need no dictionary id in the path.</summary>
public static class ChapterEndpoints
{
    public static void MapChapterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chapters").RequireAuthorization();

        // The home screen's shortcut list: every starred chapter whose book the caller can still see.
        group.MapGet("/starred", async (StarredChapterService stars, ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            return Results.Ok(await stars.GetStarredAsync(userId, role, DateTime.UtcNow));
        });

        // PUT rather than POST: starring twice is the same star, so a retry is harmless.
        group.MapPut("/{id:long}/star", async (
            long id, StarredChapterService stars, ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            // 404 rather than 403 for a chapter of an invisible book — same as GET /api/dictionaries/{id}.
            return await stars.StarAsync(userId, role, id, DateTime.UtcNow)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapDelete("/{id:long}/star", async (long id, StarredChapterService stars, ICurrentUser currentUser) =>
            await stars.UnstarAsync(await currentUser.GetIdAsync(), id)
                ? Results.NoContent()
                : Results.NotFound());
    }
}
