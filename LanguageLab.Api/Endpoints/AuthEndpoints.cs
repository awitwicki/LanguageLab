using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

/// <summary>OIDC client credentials from @BotFather's Login Widget section. Neither leaves the server.</summary>
public sealed record TelegramLoginOptions(string ClientId, string ClientSecret)
{
    /// <summary>
    /// False only in Development, where DevLogin is the other way in — Program.cs refuses to
    /// start without credentials anywhere else. Both the handler registration and the entry
    /// point below hang off this, so they cannot disagree about whether Telegram is available.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed record CurrentUserView(
    long Id, long TelegramUserId, string DisplayName, string? Username, string? PhotoUrl, UserRole Role);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // The handler owns /api/auth/telegram/callback; this is only the way in. When the
        // credentials are absent the handler is not registered at all, so challenging its
        // scheme would throw — say what is wrong instead.
        group.MapGet("/telegram/start", (TelegramLoginOptions telegram) => telegram.IsConfigured
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [TelegramAuth.Scheme])
            : Results.Problem(
                "Telegram sign-in is not configured: Telegram:ClientId and Telegram:ClientSecret " +
                "are unset. Only possible in Development — use the local dev sign-in instead.",
                statusCode: StatusCodes.Status503ServiceUnavailable));

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

        // Self-deletion. The admin endpoints refuse to act on the caller, so this is its own
        // path; the last-admin rule still applies, and answers the same way they do.
        group.MapDelete("/me", async (
            HttpContext http, ICurrentUserContext currentUser, AccountService accounts) =>
        {
            var result = await accounts.DeleteOwnAsync(currentUser.Require().Id);

            switch (result)
            {
                case AccountDeleteResult.NotFound:
                    return Results.Unauthorized();

                case AccountDeleteResult.LastAdmin:
                    return Results.Json(
                        new AdminError("This is the last administrator — promote someone else first."),
                        statusCode: StatusCodes.Status409Conflict);
            }

            // The row is gone, so SessionValidator would reject the next request anyway —
            // but the cookie should not outlive the account.
            await http.SignOutAsync(PrincipalFactory.Scheme);
            return Results.NoContent();
        }).RequireAuthorization();

#if DEBUG
        // Sign in locally without Telegram. Compiled out of Release builds and, on top of
        // that, only mapped in Development — see DevLogin for why both fences are there.
        if (DevLogin.IsEnabled(app.Environment))
        {
            group.MapGet("/dev-login", async (HttpContext http, UserLoginService login) =>
            {
                var result = await login.LoginAsync(DevLogin.Identity, DateTime.UtcNow);

                // Reached by a browser navigation, so the answer is a redirect either way —
                // the same two landings the OIDC callback uses.
                if (result.Outcome == LoginOutcome.Banned)
                {
                    return Results.Redirect("/?error=banned");
                }

                await http.SignInAsync(
                    PrincipalFactory.Scheme,
                    PrincipalFactory.Create(result.User.Id, result.User.Role));

                return Results.Redirect("/");
            });
        }
#endif
    }

    // DisplayName is computed on the entity (Task 1) so the admin list gives the same answer.
    private static CurrentUserView ToView(TelegramUser user) =>
        new(user.Id, user.TelegramUserId, user.DisplayName, user.Username, user.PhotoUrl, user.Role);
}
