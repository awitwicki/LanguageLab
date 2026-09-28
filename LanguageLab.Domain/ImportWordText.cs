namespace LanguageLab.Domain;

/// <summary>
/// What a word coming from a book import may look like. Stricter than <see cref="WordText"/>,
/// which also serves hand-typed entries: the server tokenizer (ImportTokenizer) emits only
/// lowercase ASCII lemmas the English lexicon knows, and the lexicon has real two-letter lemmas
/// (go, ox) — the lexicon, not this length rule, is the junk filter. A shared WordPair row is
/// global and outlives the dictionary that created it, so this is the one place to stop anything
/// a tokenizer of ours could never have produced.
/// </summary>
public static class ImportWordText
{
    public const int MinLength = 2;
    public const int MaxLength = WordText.MaxLength;

    /// <summary>Refuse the whole import once this share of its distinct words is unusable.</summary>
    public const double MaxJunkShare = 0.2;

    /// <summary>Expects the already trimmed, lowercased form the importer produces.</summary>
    public static bool IsValid(string normalized) =>
        normalized.Length is >= MinLength and <= MaxLength
        && normalized.All(c => c is >= 'a' and <= 'z');
}
