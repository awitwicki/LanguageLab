using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// The other side of MyMemorySentenceBudget: the share of MyMemory's daily quota that word
/// lookups may spend. Without it one account looping over GET /api/translate empties the day
/// for everybody, and the reader's word panel goes with it. A thin wrapper around the shared
/// <see cref="DailyCharacterBudget"/> mechanism, which also backs MyMemorySentenceBudget.
/// </summary>
public sealed class MyMemoryWordBudget
{
    /// <summary>The remainder of the provider's day, once sentences have taken theirs.</summary>
    public const double Share = 0.6;

    private readonly DailyCharacterBudget _budget;

    public MyMemoryWordBudget(IOptions<TranslationOptions> options) =>
        _budget = new DailyCharacterBudget((int)(MyMemoryDailyLimits.For(options.Value) * Share));

    /// <summary>
    /// True, and the characters spent, when they fit what remains of today's budget; false and
    /// nothing spent otherwise. Resets the moment the UTC calendar day changes.
    /// </summary>
    public bool TryConsume(int characters, DateTime nowUtc) => _budget.TryConsume(characters, nowUtc);
}
