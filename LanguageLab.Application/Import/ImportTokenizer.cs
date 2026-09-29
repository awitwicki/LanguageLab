using System.Text.RegularExpressions;
using LanguageLab.Application.Books;
using LanguageLab.Application.Services;
using LanguageLab.Domain;
using LanguageLab.Domain.Lexicon;

namespace LanguageLab.Application.Import;

/// <summary>One chapter's lemma counts, in the chapter's own flatten order.</summary>
internal sealed record TokenizedChapter(int Order, string Title, IReadOnlyList<ImportWord> Words);

/// <summary>
/// The tokenized book plus the evidence the not-English gate weighs: how many token occurrences
/// the server recognized as English (stop words, number words, lexicon hits) against how many
/// clean a-z tokens it could not.
/// </summary>
internal sealed record TokenizedBook(
    IReadOnlyList<TokenizedChapter> Chapters, long KnownOccurrences, long UnknownOccurrences)
{
    /// <summary>
    /// Occurrence-weighted, not distinct-word-weighted: proper names dominate a novel's distinct
    /// words but not its occurrences, so English books pass with margin while a Latin-script
    /// non-English book fails. No countable tokens at all (a Cyrillic book) is not English.
    /// </summary>
    public bool LooksEnglish =>
        KnownOccurrences + UnknownOccurrences > 0
        && (double)KnownOccurrences / (KnownOccurrences + UnknownOccurrences) >= ImportTokenizer.MinEnglishShare;
}

/// <summary>
/// The server tokenizer behind book import — a port of web/src/fb2/tokenize.ts, change both
/// together, minus what the lexicon replaced: compromise/wink lemmatization became
/// IEnglishLexicon.LemmaOf, and consolidateIngForms is subsumed (AGID inflections already give
/// running → run).
/// </summary>
internal static partial class ImportTokenizer
{
    /// <summary>The lowest share of recognized occurrences an import may have and still be English.</summary>
    public const double MinEnglishShare = 0.5;

    /// <summary>
    /// Chapters → lemma counts plus the coverage totals. Chapter text arrives
    /// whitespace-collapsed from the C2 parsers; chapters that tokenize to nothing are dropped,
    /// keeping their Order as flattened (the import core tolerates gaps).
    /// </summary>
    public static TokenizedBook Tokenize(IReadOnlyList<ParsedChapter> chapters, IEnglishLexicon lexicon)
    {
        var result = new List<TokenizedChapter>();
        long known = 0, unknown = 0;

        foreach (var chapter in chapters)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var raw in WordBoundary().Split(chapter.Text).Where(part => part.Length > 0))
            {
                var cleaned = CleanWord(raw);
                var tokens = cleaned.Contains('-') ? SplitCompoundWord(cleaned) : [cleaned];

                foreach (var token in tokens)
                {
                    // Not a clean a-z run of MinLength+ (contractions, digits, initials):
                    // excluded from the metric entirely, exactly as the client dropped them.
                    if (token.Length < ImportWordText.MinLength || !IsAsciiLetters(token))
                    {
                        continue;
                    }

                    if (EnglishStopWords.All.Contains(token) || IsNumberWord(token))
                    {
                        known++;
                        continue;
                    }

                    if (lexicon.LemmaOf(token) is { } lemma)
                    {
                        known++;

                        // The client's isRejected runs on the already-lemmatized form (lemmatize.ts),
                        // so an inflection whose lemma is a stop or number word ("done" -> "do",
                        // "others" -> "other", "ones" -> "one") must be caught here too, not just
                        // when the surface token itself already reads as one.
                        if (!EnglishStopWords.All.Contains(lemma) && !IsNumberWord(lemma))
                        {
                            counts[lemma] = counts.GetValueOrDefault(lemma) + 1;
                        }
                    }
                    else
                    {
                        unknown++;
                    }
                }
            }

            if (counts.Count > 0)
            {
                result.Add(new TokenizedChapter(
                    chapter.Order,
                    chapter.Title,
                    counts.Select(pair => new ImportWord(pair.Key, pair.Value)).ToList()));
            }
        }

        return new TokenizedBook(result, known, unknown);
    }

    /// <summary>
    /// The apostrophe stays in the word (it is not cut out like ordinary punctuation): otherwise
    /// "don't" becomes "dont" — a valid-looking word the a-z gate no longer recognizes as a
    /// contraction. Books mostly write the typographic ’, so it is normalized to ' first.
    /// </summary>
    public static string CleanWord(string word)
    {
        var lowered = TypographicApostrophes().Replace(word.ToLowerInvariant(), "'");

        return EdgeTrim().Replace(NonWord().Replace(lowered, ""), "");
    }

    /// <summary>Each hyphen-separated part stands alone; digits are stripped as on the client.</summary>
    public static IReadOnlyList<string> SplitCompoundWord(string word)
    {
        var parts = new List<string>();

        foreach (var rawPart in word.Split('-'))
        {
            var part = Digits().Replace(CleanWord(rawPart), "");

            if (part.Length >= ImportWordText.MinLength && IsAsciiLetters(part) && !IsNumberWord(part))
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    /// <summary>one..ten bare, with an ordinal suffix (fourth, twond never occurs), and ninth.</summary>
    public static bool IsNumberWord(string word)
    {
        if (word == "ninth")
        {
            return true;
        }

        foreach (var prefix in NumberPrefixes)
        {
            if (!word.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = word[prefix.Length..];

            if (rest.Length == 0 || OrdinalSuffixes.Contains(rest))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsAsciiLetters(string token) =>
        token.Length > 0 && token.All(c => c is >= 'a' and <= 'z');

    private static readonly string[] NumberPrefixes =
        ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    private static readonly HashSet<string> OrdinalSuffixes = ["th", "st", "nd", "rd"];

    /// <summary>
    /// Whitespace plus the em and en dash: the client's tokenizer (compromise) splits words at an
    /// unspaced dash, common US dialogue typography; plain whitespace-splitting would glue the two
    /// words together, and CleanWord discards the dash character rather than replacing it.
    /// </summary>
    [GeneratedRegex(@"[\s—–]+")]
    private static partial Regex WordBoundary();

    [GeneratedRegex("[‘’]")]
    private static partial Regex TypographicApostrophes();

    [GeneratedRegex(@"[^a-z0-9_\s'-]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^[-_'""]+|[-_'""]+$")]
    private static partial Regex EdgeTrim();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}
