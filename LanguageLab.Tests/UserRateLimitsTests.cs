using System.Security.Claims;
using LanguageLab.Api;
using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Tests;

public class UserRateLimitsTests
{
    [Fact]
    public void A_user_may_spend_the_permits_and_no_more()
    {
        using var limiter = UserRateLimits.CreateLimiter(permits: 3);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire("user:1");
            Assert.True(lease.IsAcquired);
        }

        using var refused = limiter.AttemptAcquire("user:1");
        Assert.False(refused.IsAcquired);
    }

    [Fact]
    public void One_users_spending_does_not_touch_another()
    {
        using var limiter = UserRateLimits.CreateLimiter(permits: 1);

        using (var first = limiter.AttemptAcquire("user:1"))
        {
            Assert.True(first.IsAcquired);
        }

        using var second = limiter.AttemptAcquire("user:2");
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public void A_signed_in_principal_partitions_by_its_user_id()
    {
        var principal = PrincipalFactory.Create(42, UserRole.User);

        Assert.Equal("user:42", UserRateLimits.PartitionKey(principal));
    }

    // Review Focus 3: the limiter must not throw or lump everyone together when authentication
    // has not produced a user — a null principal is a real state on an unauthenticated request.
    [Fact]
    public void A_request_with_no_user_falls_into_the_anonymous_partition()
    {
        Assert.Equal("anonymous", UserRateLimits.PartitionKey(null));
        Assert.Equal("anonymous", UserRateLimits.PartitionKey(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
