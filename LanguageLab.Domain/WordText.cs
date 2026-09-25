namespace LanguageLab.Domain;

/// <summary>
/// The one spelling a hand-typed word is stored and looked up under: trimmed, inner runs of
/// whitespace collapsed to one space, lowercased. Multi-word entries ("give up") are fine.
/// The book pipeline lowercases on the client, so a personal "Apple " and a book "apple"
/// end up the same text.
/// </summary>
public static class WordText
{
    public const int MaxLength = 64;

    public static string Normalize(string raw)
    {
        var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    /// <summary>Letters, spaces, hyphens and apostrophes only, 1..MaxLength characters. Expects the normalized form.</summary>
    public static bool IsValid(string normalized) =>
        normalized.Length is > 0 and <= MaxLength
        && normalized.All(c => char.IsLetter(c) || c is ' ' or '-' or '\'' or '\u2019');
}
