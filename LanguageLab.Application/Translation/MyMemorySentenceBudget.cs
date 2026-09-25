using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// A server-wide daily character budget for sentences MyMemory translates. MyMemory's daily
/// quota (see <see cref="MyMemoryDailyLimits"/>) is shared with the word lookups
/// (MyMemoryTranslator, via MyMemoryWordBudget), while one reader alone may spend up to
/// 20 000 characters a day (SentenceQuota) — enough to exhaust the whole day and break word
/// translations for everybody. So sentences get a budget of their own: 40 % of MyMemory's daily
/// limit, checked before a sentence is sent, spent only on a sentence that actually goes out.
/// The day is the UTC calendar day; a process restart forgives everybody, which is acceptable.
/// Registered as a singleton, so TryConsume must be thread-safe. A thin wrapper around the
/// shared <see cref="DailyCharacterBudget"/> mechanism, which also backs MyMemoryWordBudget.
/// </summary>
public sealed class MyMemorySentenceBudget
{
    /// <summary>The share of MyMemory's daily limit sentences may spend.</summary>
    public const double Share = 0.4;

    private readonly DailyCharacterBudget _budget;

    public MyMemorySentenceBudget(IOptions<TranslationOptions> options) =>
        _budget = new DailyCharacterBudget((int)(MyMemoryDailyLimits.For(options.Value) * Share));

    /// <summary>
    /// True, and the characters spent, when they fit what remains of today's budget; false and
    /// nothing spent otherwise. Resets the moment the UTC calendar day changes.
    /// </summary>
    public bool TryConsume(int characters, DateTime nowUtc) => _budget.TryConsume(characters, nowUtc);
}
