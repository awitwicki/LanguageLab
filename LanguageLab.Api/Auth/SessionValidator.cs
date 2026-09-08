using LanguageLab.Infrastructure.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Auth;

/// <summary>
/// Role and identity live inside a 30-day cookie, so a ban applied today would otherwise sit
/// dormant until the cookie expired. This re-reads the user on every authenticated request:
/// one indexed lookup, and the single place where revocation happens.
/// </summary>
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var identity = PrincipalFactory.Read(context.Principal);

        if (identity == null)
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == identity.Id)
            .Select(u => new { u.Id, u.Role, u.IsBanned })
            .FirstOrDefaultAsync();

        if (user == null || user.IsBanned)
        {
            await RejectAsync(context);
            return;
        }

        // A promotion or demotion by an admin must not wait for the user to sign in again.
        if (user.Role != identity.Role)
        {
            context.ReplacePrincipal(PrincipalFactory.Create(user.Id, user.Role));
            context.ShouldRenew = true;
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(PrincipalFactory.Scheme);
    }
}
