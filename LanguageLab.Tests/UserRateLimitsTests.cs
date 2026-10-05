using System.Security.Claims;
using LanguageLab.Api;
using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Tests;

public class UserRateLimitsTests
{
    [Fact]
    public void Anonymous_requests_are_partitioned_by_client_ip()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        Assert.Equal("ip:203.0.113.7", UserRateLimits.IpPartitionKey(context));
    }

    /// <summary>One IPv6 subscriber holds a whole /64; per-address buckets would give them unlimited ones.</summary>
    [Fact]
    public void Ipv6_clients_share_a_bucket_per_64_block()
    {
        var first = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        first.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8:1:2:aaaa::1");
        var second = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        second.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8:1:2:bbbb::9");

        Assert.Equal("ip:2001:db8:1:2::/64", UserRateLimits.IpPartitionKey(first));
        Assert.Equal(UserRateLimits.IpPartitionKey(first), UserRateLimits.IpPartitionKey(second));
    }

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
        var principal = PrincipalFactory.Create(42, UserRole.User, 0, DateTimeOffset.UtcNow);

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

    [Fact]
    public void A_refused_lease_names_the_wait()
    {
        // With permits: 1, the only permit does not free up until the segment holding it leaves
        // the window — 23-24 hours away for a fresh window, not "the next segment" (1 hour). A
        // fallback below the actual wait would tell the caller to retry far too early.
        using var limiter = UserRateLimits.CreateLimiter(permits: 1);
        using var spent = limiter.AttemptAcquire("user:1");
        using var refused = limiter.AttemptAcquire("user:1");

        Assert.False(refused.IsAcquired);
        Assert.True(UserRateLimits.TryGetRetryAfter(refused, out var retryAfter));
        Assert.True(retryAfter >= TimeSpan.FromHours(23), $"expected at least 23h, got {retryAfter}");
    }
}
