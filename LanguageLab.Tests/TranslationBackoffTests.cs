using LanguageLab.Application.Translation.Queue;

namespace LanguageLab.Tests;

public class TranslationBackoffTests
{
    /// <summary>Minutes in, minutes out; null = not given.</summary>
    [Theory]
    [InlineData(5.0, null, 5.0)]        // the provider's Retry-After wins…
    [InlineData(5.0, 32.0, 5.0)]        // …whatever came before
    [InlineData(null, null, 1.0)]       // first refusal without a hint: one minute
    [InlineData(null, 1.0, 2.0)]        // then doubling
    [InlineData(null, 2.0, 4.0)]
    [InlineData(null, 32.0, 60.0)]      // capped at an hour
    [InlineData(null, 60.0, 60.0)]
    [InlineData(0.0, null, 1.0)]        // a zero Retry-After is no hint at all — never a busy loop
    [InlineData(2880.0, null, 1440.0)]  // two days is capped at one, which Task.Delay can take
    public void Next_wait(double? retryAfterMinutes, double? previousMinutes, double expectedMinutes)
    {
        var next = TranslationBackoff.Next(
            retryAfterMinutes is { } retryAfter ? TimeSpan.FromMinutes(retryAfter) : null,
            previousMinutes is { } previous ? TimeSpan.FromMinutes(previous) : null);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), next);
    }
}
