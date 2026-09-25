using LanguageLab.Api;

namespace LanguageLab.Tests;

public class SentenceQuotaTests
{
    [Fact]
    public void Characters_are_spent_until_the_day_is_used_up()
    {
        using var quota = new SentenceQuota(characters: 100, window: TimeSpan.FromDays(1));

        Assert.True(quota.TryConsume(1, 60));
        Assert.True(quota.TryConsume(1, 40));
        Assert.False(quota.TryConsume(1, 1));
    }

    [Fact]
    public void Every_user_has_their_own_day()
    {
        using var quota = new SentenceQuota(characters: 100, window: TimeSpan.FromDays(1));

        Assert.True(quota.TryConsume(1, 100));
        Assert.True(quota.TryConsume(2, 100));
    }

    /// <summary>The limiter throws on a request larger than its whole budget; the quota must refuse instead.</summary>
    [Fact]
    public void A_request_bigger_than_the_whole_day_is_refused()
    {
        using var quota = new SentenceQuota(characters: 100, window: TimeSpan.FromDays(1));

        Assert.False(quota.TryConsume(1, 101));
        Assert.True(quota.TryConsume(1, 100));
    }

    [Fact]
    public void The_default_is_twenty_thousand_characters()
    {
        Assert.Equal(20_000, SentenceQuota.CharactersPerDay);
    }
}
