using System.Globalization;
using System.Security.Claims;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LanguageLab.Api.Auth;

/// <summary>
/// The only place that knows how a user id and role are spelled inside the session cookie.
/// Sign-in, the per-request validator and ICurrentUser all go through here, so the claim
/// names cannot drift apart.
/// </summary>
public static class PrincipalFactory
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static ClaimsPrincipal Create(long userId, UserRole role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, role.ToString()),
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
}
