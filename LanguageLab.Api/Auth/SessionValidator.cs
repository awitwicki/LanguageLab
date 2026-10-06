using System.Security.Claims;
using LanguageLab.Api;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Auth;

/// <summary>
/// Role and identity live inside a 30-day cookie, so a ban applied today would otherwise sit
/// dormant until the cookie expired. This re-reads the user on every authenticated request:
/// one indexed lookup, and the single place where revocation happens — a ban, a deleted account,
/// a logout elsewhere (the session version moved on) or a session past its absolute lifetime.
/// </summary>
public static class SessionValidator
{
    /// <summary>
    /// The cookie slides for 30 days of inactivity; this caps a session however active it stays,
    /// so a stolen cookie that keeps being used still dies.
    /// </summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(90);

    public static bool IsCurrent(SessionStamp stamp, int currentVersion, DateTimeOffset now) =>
        stamp.Version == currentVersion && now - stamp.IssuedAt <= AbsoluteLifetime;

    /// <summary>A new role, the same session: the version and the original issue time carry over.</summary>
    internal static ClaimsPrincipal Reissue(long userId, UserRole role, SessionStamp stamp) =>
        PrincipalFactory.Create(userId, role, stamp.Version, stamp.IssuedAt);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var identity = PrincipalFactory.Read(context.Principal);
        var stamp = PrincipalFactory.ReadStamp(context.Principal);

        if (identity == null || stamp == null)
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == identity.Id)
            .Select(u => new { u.Id, u.Role, u.IsBanned, u.Language, u.SessionVersion })
            .FirstOrDefaultAsync();

        if (user == null || user.IsBanned || !IsCurrent(stamp, user.SessionVersion, DateTimeOffset.UtcNow))
        {
            await RejectAsync(context);
            return;
        }

        // Read by ICurrentLanguage — this row is loaded here on every request anyway.
        context.HttpContext.Items[HttpCurrentLanguage.ItemKey] = user.Language;

        // A promotion or demotion by an admin must not wait for the user to sign in again.
        if (user.Role != identity.Role)
        {
            context.ReplacePrincipal(Reissue(user.Id, user.Role, stamp));
            context.ShouldRenew = true;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(PrincipalFactory.Scheme);
    }
}
