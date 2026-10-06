using System.Globalization;
using System.Security.Claims;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LanguageLab.Api.Auth;

/// <summary>
/// The only place that knows how a user id, role and session stamp are spelled inside the session cookie.
/// Sign-in, the per-request validator and ICurrentUser all go through here, so the claim
/// names cannot drift apart.
/// </summary>
public static class PrincipalFactory
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    private const string SessionVersionClaim = "ll:sv";
    private const string IssuedAtClaim = "ll:iat";

    public static ClaimsPrincipal Create(long userId, UserRole role, int sessionVersion, DateTimeOffset issuedAt) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, role.ToString()),
                new Claim(SessionVersionClaim, sessionVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim(IssuedAtClaim, issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ],
            Scheme));

    /// <summary>Null when the cookie is absent or malformed — treated the same as signed out.</summary>
    public static CurrentUserContext? Read(ClaimsPrincipal? principal)
    {
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = principal?.FindFirstValue(ClaimTypes.Role);

        if (!long.TryParse(id, CultureInfo.InvariantCulture, out var userId) ||
            !Enum.TryParse<UserRole>(role, out var parsedRole))
        {
            return null;
        }

        return new CurrentUserContext(userId, parsedRole);
    }

    /// <summary>Null when the cookie predates session stamps or is malformed — not a session.</summary>
    public static SessionStamp? ReadStamp(ClaimsPrincipal? principal)
    {
        if (!int.TryParse(principal?.FindFirstValue(SessionVersionClaim), CultureInfo.InvariantCulture, out var version) ||
            !long.TryParse(principal?.FindFirstValue(IssuedAtClaim), CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return new SessionStamp(version, DateTimeOffset.FromUnixTimeSeconds(seconds));
    }
}

/// <summary>The <see cref="TelegramUser.SessionVersion"/> a cookie was issued under, and when.</summary>
public sealed record SessionStamp(int Version, DateTimeOffset IssuedAt);
