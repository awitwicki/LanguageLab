using System.Globalization;
using System.Security.Claims;
using LanguageLab.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace LanguageLab.Api.Auth;

/// <summary>
/// Where the OIDC handshake becomes an application session. The handler has already
/// validated the id_token's signature, issuer, audience, expiry and nonce by the time
/// anything here runs, so these are claims, not input.
/// </summary>
public static class TelegramAuth
{
    public const string Scheme = "Telegram";

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var identity = ReadIdentity(context.Principal);

        if (identity == null)
        {
            context.Fail("Telegram returned no usable user id.");
            return;
        }

        var login = context.HttpContext.RequestServices.GetRequiredService<UserLoginService>();
        var result = await login.LoginAsync(identity, DateTime.UtcNow);

        if (result.Outcome == LoginOutcome.Banned)
        {
            // The callback is a redirect, not a fetch, so there is no 403 body to send.
            // The SPA reads this parameter once at boot and shows the banned screen.
            context.HandleResponse();
            context.Response.Redirect("/?error=banned");
            return;
        }

        // The session carries our identity, not Telegram's: the id_token's claims stop here,
        // and the cookie holds only an internal user id and a role.
        context.Principal = PrincipalFactory.Create(result.User.Id, result.User.Role);
    }

    /// <summary>A cancelled consent or a provider error should land on the login screen, not a stack trace.</summary>
    public static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();
        context.Response.Redirect("/?error=login");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Telegram splits the identifiers: `openid` yields `sub` (an opaque subject the discovery
    /// document advertises), while the numeric Telegram user id arrives as `id` under the
    /// `profile` scope — see the decoded id_token sample at
    /// https://core.telegram.org/bots/telegram-login. TelegramUserId must be the latter:
    /// existing rows hold real Telegram ids, and keying on `sub` would orphan their shelves.
    /// </summary>
    internal static TelegramIdentity? ReadIdentity(ClaimsPrincipal? principal)
    {
        if (principal == null ||
            !long.TryParse(principal.FindFirstValue("id"), CultureInfo.InvariantCulture, out var telegramUserId))
        {
            return null;
        }

        // given_name/family_name are the OIDC spelling; `name` is the fallback Telegram
        // always sends with the profile scope.
        return new TelegramIdentity(
            telegramUserId,
            principal.FindFirstValue("given_name") ?? principal.FindFirstValue("name"),
            principal.FindFirstValue("family_name"),
            principal.FindFirstValue("preferred_username"),
            principal.FindFirstValue("picture"));
    }
}
