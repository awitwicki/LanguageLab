using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

/// <summary>OIDC client credentials from @BotFather's Login Widget section. Neither leaves the server.</summary>
public sealed record TelegramLoginOptions(string ClientId, string ClientSecret);

public sealed record CurrentUserView(
    long Id, long TelegramUserId, string DisplayName, string? Username, string? PhotoUrl, UserRole Role);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // The handler owns /api/auth/telegram/callback; this is only the way in.
        group.MapGet("/telegram/start", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [TelegramAuth.Scheme]));

        group.MapGet("/me", async (ICurrentUserContext currentUser, ApplicationDbContext db) =>
        {
            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == currentUser.Require().Id);

            return user == null ? Results.Unauthorized() : Results.Ok(ToView(user));
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(PrincipalFactory.Scheme);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    // DisplayName is computed on the entity (Task 1) so the admin list gives the same answer.
    private static CurrentUserView ToView(TelegramUser user) =>
        new(user.Id, user.TelegramUserId, user.DisplayName, user.Username, user.PhotoUrl, user.Role);
}
