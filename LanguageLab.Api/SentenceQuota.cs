using System.Threading.RateLimiting;

namespace LanguageLab.Api;

/// <summary>
/// How many characters of sentence translation one user may spend a day. DeepL's free tier is
/// 500 000 characters a month for the whole app, so one avid reader must not spend it for
/// everyone. Counted in characters, not requests — which is why it is a limiter the endpoint
/// asks itself rather than the rate-limiting middleware, which counts requests. In memory: a
/// restart forgives everybody, which is acceptable.
/// </summary>
public sealed class SentenceQuota : IDisposable
{
    public const int CharactersPerDay = 20_000;

    private readonly int _characters;
    private readonly PartitionedRateLimiter<long> _limiter;

    public SentenceQuota()
        : this(CharactersPerDay, TimeSpan.FromDays(1))
    {
    }

    public SentenceQuota(int characters, TimeSpan window)
    {
        _characters = characters;
        _limiter = PartitionedRateLimiter.Create<long, long>(userId =>
            RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = characters,
                Window = window,
                SegmentsPerWindow = 24,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public bool TryConsume(long userId, int characters)
    {
        // The limiter throws on a request larger than its whole budget.
        if (characters > _characters)
        {
            return false;
        }

        using var lease = _limiter.AttemptAcquire(userId, characters);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
