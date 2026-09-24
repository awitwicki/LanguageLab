using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// A server-wide daily character budget for sentences MyMemory translates. MyMemory's daily
/// quota (5 000 characters per server IP anonymously, 50 000 with Translation:MyMemoryEmail) is
/// shared with the word lookups (MyMemoryTranslator), while one reader alone may spend up to
/// 20 000 characters a day (SentenceQuota) — enough to exhaust the whole day and break word
/// translations for everybody. So sentences get a budget of their own: 40 % of MyMemory's daily
/// limit, checked before a sentence is sent, spent only on a sentence that actually goes out.
/// The day is the UTC calendar day; a process restart forgives everybody, which is acceptable.
/// Registered as a singleton, so TryConsume must be thread-safe.
/// </summary>
public sealed class MyMemorySentenceBudget
{
    /// <summary>MyMemory's own daily limit per server IP without a contact email.</summary>
    public const int AnonymousDailyLimit = 5_000;

    /// <summary>MyMemory's own daily limit per server IP once Translation:MyMemoryEmail rides along.</summary>
    public const int EmailDailyLimit = 50_000;

    /// <summary>The share of MyMemory's daily limit sentences may spend.</summary>
    public const double Share = 0.4;

    private readonly int _dailyLimit;
    private readonly object _gate = new();
    private DateOnly _day;
    private int _spent;

    public MyMemorySentenceBudget(IOptions<TranslationOptions> options)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(options.Value.MyMemoryEmail);
        _dailyLimit = (int)((hasEmail ? EmailDailyLimit : AnonymousDailyLimit) * Share);
    }

    /// <summary>
    /// True, and the characters spent, when they fit what remains of today's budget; false and
    /// nothing spent otherwise. Resets the moment the UTC calendar day changes.
    /// </summary>
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

            if (_spent + characters > _dailyLimit)
            {
                return false;
            }

            _spent += characters;
            return true;
        }
    }
}
