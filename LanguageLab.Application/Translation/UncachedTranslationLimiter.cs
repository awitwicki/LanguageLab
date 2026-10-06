using System.Collections.Concurrent;

namespace LanguageLab.Application.Translation;

/// <summary>
/// One uncached translation per user every <see cref="Window"/>, and at most
/// <see cref="DailyLimit"/> per UTC day, admins included: a sentence, a reader lookup, a sorting
/// mark and a word-lookup miss all reach the language model, so they share one budget. In memory,
/// one entry per user who translated since the start — a restart forgives everybody. The slot is
/// claimed with a compare-and-swap, so two concurrent misses from one user cannot both get through.
/// </summary>
public sealed class UncachedTranslationLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Without it the 10 s pace alone allowed ~8,600 paid calls per account per day. A learner
    /// reading all day looks up far fewer new words than this; most lookups are cache hits, which
    /// are free and never counted.
    /// </summary>
    public const int DailyLimit = 300;

    private readonly ConcurrentDictionary<long, Spent> _spent = new();
    private readonly TimeProvider _time;

    public UncachedTranslationLimiter(TimeProvider time) => _time = time;

    /// <summary>True and the slot spent; false and <paramref name="retryAfter"/> until it frees up.</summary>
    public bool TryConsume(long userId, out TimeSpan retryAfter)
    {
        while (true)
        {
            var now = _time.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);

            if (!_spent.TryGetValue(userId, out var spent))
            {
                if (_spent.TryAdd(userId, new Spent(now, today, 1)))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                continue;
            }

            var wait = spent.Last + Window - now;

            if (wait > TimeSpan.Zero)
            {
                retryAfter = wait;
                return false;
            }

            var count = spent.Day == today ? spent.Count : 0;

            if (count >= DailyLimit)
            {
                retryAfter = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) - now;
                return false;
            }

            if (_spent.TryUpdate(userId, new Spent(now, today, count + 1), spent))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }

    /// <summary>Whole seconds, rounded up — what Retry-After and the SPA show.</summary>
    public static int Seconds(TimeSpan wait) => (int)Math.Ceiling(wait.TotalSeconds);

    private sealed record Spent(DateTimeOffset Last, DateOnly Day, int Count);
}
