using LanguageLab.Application.Translation;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests;

public class UncachedTranslationLimiterTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-29T12:00:00Z");

    [Fact]
    public void The_first_translation_goes_through()
    {
        var limiter = new UncachedTranslationLimiter(new ManualTimeProvider(Start));

        Assert.True(limiter.TryConsume(1, out var wait));
        Assert.Equal(TimeSpan.Zero, wait);
    }

    [Fact]
    public void A_second_one_inside_the_window_is_refused_with_the_exact_wait()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UncachedTranslationLimiter(clock);
        limiter.TryConsume(1, out _);

        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.False(limiter.TryConsume(1, out var wait));
        Assert.Equal(TimeSpan.FromSeconds(7), wait);
    }

    [Fact]
    public void A_refusal_does_not_restart_the_window()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UncachedTranslationLimiter(clock);
        limiter.TryConsume(1, out _);

        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.False(limiter.TryConsume(1, out _));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryConsume(1, out _));
    }

    [Fact]
    public void Every_user_has_their_own_window()
    {
        var limiter = new UncachedTranslationLimiter(new ManualTimeProvider(Start));

        Assert.True(limiter.TryConsume(1, out _));
        Assert.True(limiter.TryConsume(2, out _));
    }

    [Fact]
    public void Concurrent_misses_from_one_user_let_exactly_one_through()
    {
        var limiter = new UncachedTranslationLimiter(new ManualTimeProvider(Start));
        var passed = 0;

        Parallel.For(0, 64, i =>
        {
            if (limiter.TryConsume(1, out _))
            {
                Interlocked.Increment(ref passed);
            }
        });

        Assert.Equal(1, passed);
    }

    [Theory]
    [InlineData(10_000, 10)]
    [InlineData(6_001, 7)]
    [InlineData(1, 1)]
    public void Seconds_round_up(int milliseconds, int expected)
    {
        Assert.Equal(expected, UncachedTranslationLimiter.Seconds(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void The_window_is_ten_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), UncachedTranslationLimiter.Window);
    }

    private static void SpendTheDay(UncachedTranslationLimiter limiter, ManualTimeProvider clock, long userId)
    {
        for (var i = 0; i < UncachedTranslationLimiter.DailyLimit; i++)
        {
            Assert.True(limiter.TryConsume(userId, out _));
            clock.Advance(UncachedTranslationLimiter.Window);
        }
    }

    [Fact]
    public void The_daily_cap_refuses_until_utc_midnight()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UncachedTranslationLimiter(clock);
        SpendTheDay(limiter, clock, 1);

        Assert.False(limiter.TryConsume(1, out var wait));
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T00:00:00Z") - clock.GetUtcNow(), wait);
    }

    [Fact]
    public void The_daily_cap_resets_the_next_utc_day()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UncachedTranslationLimiter(clock);
        SpendTheDay(limiter, clock, 1);

        clock.Advance(TimeSpan.FromHours(12));

        Assert.True(limiter.TryConsume(1, out _));
    }

    [Fact]
    public void One_users_daily_cap_does_not_touch_another()
    {
        var clock = new ManualTimeProvider(Start);
        var limiter = new UncachedTranslationLimiter(clock);
        SpendTheDay(limiter, clock, 1);

        Assert.True(limiter.TryConsume(2, out _));
    }
}
