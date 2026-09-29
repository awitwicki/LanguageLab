using System.Collections.Concurrent;

namespace LanguageLab.Application.Translation;

/// <summary>
/// One uncached translation per user every <see cref="Window"/>, admins included: a sentence and a
/// word-lookup miss both reach the language model, so they share one slot. In memory, one instant
/// per user who translated since the start — a restart forgives everybody. The slot is claimed
/// with a compare-and-swap, so two concurrent misses from one user cannot both get through.
/// </summary>
public sealed class UncachedTranslationLimiter
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<long, DateTimeOffset> _lastSpent = new();
    private readonly TimeProvider _time;

    public UncachedTranslationLimiter(TimeProvider time) => _time = time;

    /// <summary>True and the slot spent; false and <paramref name="retryAfter"/> until it frees up.</summary>
    public bool TryConsume(long userId, out TimeSpan retryAfter)
    {
        while (true)
        {
            var now = _time.GetUtcNow();

            if (!_lastSpent.TryGetValue(userId, out var last))
            {
                if (_lastSpent.TryAdd(userId, now))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                continue;
            }

            var wait = last + Window - now;

            if (wait > TimeSpan.Zero)
            {
                retryAfter = wait;
                return false;
            }

            if (_lastSpent.TryUpdate(userId, now, last))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }

    /// <summary>Whole seconds, rounded up — what Retry-After and the SPA show.</summary>
    public static int Seconds(TimeSpan wait) => (int)Math.Ceiling(wait.TotalSeconds);
}
