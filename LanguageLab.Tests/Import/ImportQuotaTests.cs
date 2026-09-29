using System.Collections.Concurrent;
using LanguageLab.Application.Import;
using LanguageLab.Domain.Entities;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests.Import;

public class ImportQuotaTests
{
    [Fact]
    public void A_user_who_never_imported_may_reserve_now()
    {
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));

        Assert.Null(quota.TryReserve(1, UserRole.User));
    }

    [Fact]
    public void A_reservation_blocks_a_second_one_for_the_whole_window()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var quota = new ImportQuota(time);

        quota.TryReserve(1, UserRole.User);

        Assert.Equal(ImportQuota.Window, quota.TryReserve(1, UserRole.User));
        Assert.Equal(ImportQuota.Window, quota.RetryAfter(1, UserRole.User));
    }

    [Fact]
    public void The_wait_counts_down_and_clears_once_the_window_passes()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var quota = new ImportQuota(time);
        quota.TryReserve(1, UserRole.User);

        time.Advance(TimeSpan.FromHours(23));
        Assert.Equal(TimeSpan.FromHours(1), quota.RetryAfter(1, UserRole.User));

        time.Advance(TimeSpan.FromHours(1));
        Assert.Null(quota.RetryAfter(1, UserRole.User));
        Assert.Null(quota.TryReserve(1, UserRole.User));
    }

    [Fact]
    public void Every_user_has_their_own_window()
    {
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        quota.TryReserve(1, UserRole.User);

        Assert.Null(quota.TryReserve(2, UserRole.User));
    }

    [Fact]
    public void An_admin_is_exempt_even_after_reserving()
    {
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        quota.TryReserve(1, UserRole.Admin);

        Assert.Null(quota.TryReserve(1, UserRole.Admin));
    }

    [Fact]
    public void Releasing_a_reservation_frees_the_slot_immediately()
    {
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        quota.TryReserve(1, UserRole.User);

        quota.ReleaseReservation(1);

        Assert.Null(quota.RetryAfter(1, UserRole.User));
        Assert.Null(quota.TryReserve(1, UserRole.User));
    }

    // Final review, Important 1: the check and the reservation must be one atomic step, or N
    // concurrent requests for the same user can all pass the check before any of them commits.
    [Fact]
    public void Concurrent_reservations_for_the_same_user_let_only_one_through()
    {
        var quota = new ImportQuota(TimeProvider.System);
        var results = new ConcurrentBag<TimeSpan?>();

        Parallel.For(0, 50, _ => results.Add(quota.TryReserve(1, UserRole.User)));

        Assert.Single(results, r => r is null);
    }
}
