using System.Security.Claims;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace LanguageLab.Api.Auth;

/// <summary>
/// The named policies endpoints hang on. Roles reach here as the session cookie's role
/// claim (PrincipalFactory), so a policy is a list of the role names it admits.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Curates dictionaries and the user list.</summary>
    public const string Admin = nameof(UserRole.Admin);

    /// <summary>May import a book: uploaders, and admins as a superset.</summary>
    public const string Importer = "Importer";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Admin, policy => policy.RequireClaim(ClaimTypes.Role, nameof(UserRole.Admin)));
        options.AddPolicy(Importer, policy => policy.RequireClaim(
            ClaimTypes.Role, nameof(UserRole.Admin), nameof(UserRole.Uploader)));
    }
}
