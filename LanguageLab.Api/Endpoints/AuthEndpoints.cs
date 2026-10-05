using LanguageLab.Api;
using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
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

/// <summary>
/// The bot token from @BotFather. Telegram signs a Mini App's launch parameters with it, so it
/// is what POST /api/auth/telegram/webapp checks them against. It never leaves the server.
/// </summary>
public sealed record TelegramWebAppOptions(string BotToken)
{
    /// <summary>False only in Development — Program.cs refuses to start without the token anywhere else.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}

public sealed record CurrentUserView(
    long Id, long TelegramUserId, string DisplayName, string? Username, string? PhotoUrl, UserRole Role,
    string? Language, string? SuggestedLanguage, int? VerbsWordCount);

/// <summary>The body of POST /api/auth/telegram/webapp: window.Telegram.WebApp.initData, verbatim.</summary>
public sealed record WebAppLoginRequest(string? InitData);

public sealed record SetLanguageRequest(string? Code);

public sealed record SetVerbsWordCountRequest(int Words);

public sealed record WordCountError(string Error);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // The handler owns /api/auth/telegram/callback; this is only the way in. When the
        // credentials are absent the handler is not registered at all, so challenging its
        // scheme would throw — say what is wrong instead.
        // Both anonymous entry points are rate-limited per client address.
        group.MapGet("/telegram/start", (TelegramLoginOptions telegram, ServerSideStateFormat state) =>
        {
            if (!telegram.IsConfigured)
            {
                return Results.Problem(
                    "Telegram sign-in is not configured: Telegram:ClientId and Telegram:ClientSecret " +
                    "are unset. Only possible in Development — use the local dev sign-in instead.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return state.IsFull
                ? Results.Problem(
                    "Too many sign-ins in progress. Try again in a minute.",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [TelegramAuth.Scheme]);
        }).RequireRateLimiting(UserRateLimits.AnonymousAuth);

        group.MapPost("/telegram/webapp", SignInFromWebAppAsync).RequireRateLimiting(UserRateLimits.AnonymousAuth);

        group.MapGet("/me", async (ICurrentUserContext currentUser, ApplicationDbContext db) =>
        {
            var user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == currentUser.Require().Id);

            return user == null ? Results.Unauthorized() : Results.Ok(ToView(user));
        }).RequireAuthorization();

        group.MapPut("/me/language", async (
            SetLanguageRequest body, ICurrentUserContext currentUser, AccountService accounts) =>
            await accounts.SetLanguageAsync(currentUser.Require().Id, body.Code) switch
            {
                SetLanguageResult.Saved => Results.NoContent(),
                SetLanguageResult.NotFound => Results.Unauthorized(),
                _ => Results.Json(new LanguageError("unknown_language"), statusCode: StatusCodes.Status400BadRequest),
            }).RequireAuthorization();

        group.MapPut("/me/verbs-word-count", async (
            SetVerbsWordCountRequest body, ICurrentUserContext currentUser, AccountService accounts) =>
            await accounts.SetVerbsWordCountAsync(currentUser.Require().Id, body.Words) switch
            {
                SetVerbsWordCountResult.Saved => Results.NoContent(),
                SetVerbsWordCountResult.NotFound => Results.Unauthorized(),
                _ => Results.Json(new WordCountError("invalid_value"), statusCode: StatusCodes.Status400BadRequest),
            }).RequireAuthorization();

        // Server-side, not just the browser's copy: a copied cookie dies too. That signs this
        // account out on every device — the price of a cookie that cannot be revoked one by one.
        group.MapPost("/logout", async (HttpContext http, ICurrentUserContext currentUser, AccountService accounts) =>
        {
            await accounts.RevokeSessionsAsync(currentUser.Require().Id);
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
                    PrincipalFactory.Create(result.User.Id, result.User.Role, result.User.SessionVersion, DateTimeOffset.UtcNow));

                return Results.Redirect("/");
            });
        }
#endif
    }

    /// <summary>
    /// The Mini App sign-in. The SPA, opened inside Telegram's web view, posts the launch
    /// parameters Telegram signed for it; a valid signature is as good as a validated id_token,
    /// and the session it earns is the same cookie the OIDC callback issues. This is a fetch,
    /// not a redirect, so every refusal is a status code with a message.
    /// </summary>
    private static async Task<IResult> SignInFromWebAppAsync(
        WebAppLoginRequest body, HttpContext http, TelegramWebAppOptions webApp, UserLoginService login)
    {
        if (!webApp.IsConfigured)
        {
            return Results.Problem(
                "Signing in from inside Telegram is not configured: Telegram:BotToken is unset. " +
                "Only possible in Development — use the local dev sign-in instead.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var launch = WebAppInitData.Validate(body.InitData, webApp.BotToken, DateTimeOffset.UtcNow);

        if (!launch.IsValid)
        {
            // Our own message (bad signature, too old…), never the initData itself.
            TelegramAuth.Log(http).LogWarning("Mini App sign-in refused: {Reason}", launch.Error);
            return Results.Json(new AdminError(launch.Error), statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await login.LoginAsync(launch.Identity, DateTime.UtcNow);

        if (result.Outcome == LoginOutcome.Banned)
        {
            TelegramAuth.Log(http).LogInformation("Banned user {UserId} tried to sign in", result.User.Id);
            return Results.Json(
                new AdminError("An administrator has suspended this account."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        await http.SignInAsync(
            PrincipalFactory.Scheme,
            PrincipalFactory.Create(result.User.Id, result.User.Role, result.User.SessionVersion, DateTimeOffset.UtcNow));

        return Results.Ok(ToView(result.User));
    }

    // DisplayName is computed on the entity (Task 1) so the admin list gives the same answer.
    // SuggestedLanguage only matters while there is no Language: it preselects the picker.
    private static CurrentUserView ToView(TelegramUser user) =>
        new(user.Id, user.TelegramUserId, user.DisplayName, user.Username, user.PhotoUrl, user.Role,
            user.Language,
            user.Language == null ? LearnerLanguages.FromTelegram(user.TelegramLanguageCode)?.Code : null,
            user.VerbsWordCount);
}
