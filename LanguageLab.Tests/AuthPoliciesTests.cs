using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests;

/// <summary>
/// The policies are what stand between a role and an endpoint, evaluated here the way the
/// authorization middleware does — through IAuthorizationService — with the same principal
/// the session cookie produces.
/// </summary>
public class AuthPoliciesTests
{
    private static async Task<bool> AllowsAsync(string policy, UserRole role)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthPolicies.Configure)
            .BuildServiceProvider();

        var result = await services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(PrincipalFactory.Create(1, role), null, policy);

        return result.Succeeded;
    }

    [Theory]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Uploader, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task Admin_policy_admits_admins_only(UserRole role, bool expected)
    {
        Assert.Equal(expected, await AllowsAsync(AuthPolicies.Admin, role));
    }
}
