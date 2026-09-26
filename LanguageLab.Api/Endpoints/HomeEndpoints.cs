using LanguageLab.Application.Services;

namespace LanguageLab.Api.Endpoints;

/// <summary>
/// What the home screen offers to pick up again. Its own group rather than a route under
/// /api/training and another under /api/sorting: the two lists are one screen's worth of
/// state, fetched together, and /api/sorting/recent already means recent *words*.
/// </summary>
public static class HomeEndpoints
{
    public static void MapHomeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/home").RequireAuthorization();

        group.MapGet("/recent", async (RecentActivityService recent, ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            return Results.Ok(await recent.GetAsync(userId, role));
        });
    }
}
