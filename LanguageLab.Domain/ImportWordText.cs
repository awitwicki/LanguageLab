namespace LanguageLab.Domain;

/// <summary>
/// What a word coming from a book import may look like. Stricter than <see cref="WordText"/>,
/// which also serves hand-typed entries: the fb2 tokenizer (web/src/fb2/tokenize.ts, isRejected)
/// emits only lowercase ASCII runs of three letters or more, so anything else in an import
/// arrived from a client that is not ours. A shared WordPair row is global and outlives the
/// dictionary that created it, so this is the one place to stop it.
/// </summary>
public static class ImportWordText
{
    public const int MinLength = 3;
    public const int MaxLength = WordText.MaxLength;

    /// <summary>Refuse the whole import once this share of its distinct words is unusable.</summary>
    public const double MaxJunkShare = 0.2;

    /// <summary>Expects the already trimmed, lowercased form the importer produces.</summary>
    public static bool IsValid(string normalized) =>
        normalized.Length is >= MinLength and <= MaxLength
        && normalized.All(c => c is >= 'a' and <= 'z');
}
