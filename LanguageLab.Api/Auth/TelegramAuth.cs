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

    /// <summary>Guards the diagnostic claim dump below so it fires once per process, not on every sign-in.</summary>
    private static int _claimsLogged;

    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        // Telegram's discovery document advertises `sub` and `name` but never `id`, so the
        // claim names ReadIdentity expects are unconfirmed until a real sign-in happens on
        // the production domain. This dump runs before anything can reject the token, so the
        // first login leaves the actual names in the log whether it succeeds or fails.
        // Remove it once they are confirmed — it prints the profile claims verbatim.
        if (Interlocked.Exchange(ref _claimsLogged, 1) == 0)
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(TelegramAuth).FullName!);

            logger.LogInformation(
                "Telegram OIDC claims received: {Claims}",
                string.Join(", ", context.Principal?.Claims.Select(c => $"{c.Type}={c.Value}") ?? []));
        }

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
    /// Telegram splits the identifiers: `openid` yields `sub`, while the numeric Telegram
    /// user id arrives as `id` under the `profile` scope. TelegramUserId must be the latter —
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
