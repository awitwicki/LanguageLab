namespace LanguageLab.Application.Translation;

/// <summary>MyMemory's own daily quota per server IP, which every caller of it shares.</summary>
public static class MyMemoryDailyLimits
{
    public const int Anonymous = 5_000;
    public const int WithEmail = 50_000;

    public static int For(TranslationOptions options) =>
        string.IsNullOrWhiteSpace(options.MyMemoryEmail) ? Anonymous : WithEmail;
}

/// <summary>
/// A server-wide character budget for one UTC day. The day rolls over on the first call that
/// sees a new date; a process restart forgives everybody, which is acceptable. Used as a
/// singleton, so TryConsume is locked.
/// </summary>
public sealed class DailyCharacterBudget
{
    private readonly object _gate = new();
    private DateOnly _day;
    private int _spent;

    public DailyCharacterBudget(int dailyLimit) => DailyLimit = dailyLimit;

    public int DailyLimit { get; }

    /// <summary>True, and the characters spent, when they fit what is left of today; false and nothing spent otherwise.</summary>
    public bool TryConsume(int characters, DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);

        lock (_gate)
        {
            if (today != _day)
            {
                _day = today;
                _spent = 0;
            }

            if (_spent + characters > DailyLimit)
            {
                return false;
            }

            _spent += characters;
            return true;
        }
    }
}
