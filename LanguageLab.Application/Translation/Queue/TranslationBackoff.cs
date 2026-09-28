namespace LanguageLab.Application.Translation.Queue;

/// <summary>
/// How long the worker pauses after a quota refusal. The provider's Retry-After wins when it
/// gave a usable one (more than zero, capped at a day so Task.Delay can take it); otherwise one
/// minute, doubling from the previous pause, capped at an hour.
/// </summary>
public static class TranslationBackoff
{
    public static readonly TimeSpan First = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Cap = TimeSpan.FromHours(1);
    public static readonly TimeSpan LongestRetryAfter = TimeSpan.FromDays(1);

    public static TimeSpan Next(TimeSpan? retryAfter, TimeSpan? previous)
    {
        if (retryAfter is { } hint && hint > TimeSpan.Zero)
        {
            return hint < LongestRetryAfter ? hint : LongestRetryAfter;
        }

        if (previous is not { } last || last <= TimeSpan.Zero)
        {
            return First;
        }

        var doubled = last * 2;

        if (doubled < First)
        {
            return First;
        }

        return doubled < Cap ? doubled : Cap;
    }
}
