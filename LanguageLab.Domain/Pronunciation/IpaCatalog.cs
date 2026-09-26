namespace LanguageLab.Domain.Pronunciation;

/// <summary>
/// One symbol of the International Phonetic Alphabet, as the alphabet screen shows it.
/// </summary>
/// <param name="Symbol">The symbol itself — one letter, a diphthong's two, or a mark.</param>
/// <param name="Name">Its phonetic name, e.g. "voiceless palatal fricative".</param>
/// <param name="Hint">How it is read, in plain words: "sh as in \"ship\"".</param>
/// <param name="Group">The row of the chart it sits in, e.g. "Plosive", "Close-mid".</param>
/// <param name="InEnglish">Whether English uses the sound — what the screen's filter reads.</param>
/// <param name="ExampleWord">
/// A word that contains it, in <paramref name="ExampleLanguage"/>. Null for the few
/// symbols no settled example word exists for; <paramref name="Hint"/> describes those.
/// </param>
/// <param name="SoundAudioFile">The sound on its own, a file under web/public/pronunciation-audio.</param>
/// <param name="WordAudioFile">The example word said in full. Null when no recording was found.</param>
/// <param name="Aliases">
/// Other spellings a learner may look the symbol up by — a dictionary's length-marked
/// long vowel (iː for i), the r-coloured schwas (ɚ, ɝ). Search and the trainer-family
/// lookup both go through them.
/// </param>
public sealed record IpaEntry(
    string Symbol,
    string Name,
    string Hint,
    string Group,
    bool InEnglish,
    string? ExampleWord,
    string? ExampleLanguage,
    string? ExampleIpa,
    string? SoundAudioFile,
    string? WordAudioFile,
    IReadOnlyList<string> Aliases);

/// <summary>One division of the chart: pulmonic consonants, vowels, marks…</summary>
public sealed record IpaSection(string Key, string Title, string Note, IReadOnlyList<IpaEntry> Entries);

/// <summary>
/// Static data — the chart is never database rows, same as the pronunciation trainer's
/// word catalog. Sections are declared in IpaCatalog.Generated.cs, produced by
/// generate_ipa_catalog.py; this file only adds lookup helpers on top and is never
/// regenerated.
/// </summary>
public static partial class IpaCatalog
{
    // Both are computed on first use rather than at type initialization: Sections is
    // declared in the other half of this partial class, and the order static initializers
    // run in across a partial's files is the compiler's business, not something to rely on.
    private static readonly Lazy<IReadOnlyList<IpaEntry>> AllEntries =
        new(() => Sections.SelectMany(s => s.Entries).ToList());

    private static readonly Lazy<Dictionary<string, IpaEntry>> SpellingIndex = new(BuildSpellingIndex);

    /// <summary>Every symbol, in chart order, flattened out of the sections.</summary>
    public static IReadOnlyList<IpaEntry> All => AllEntries.Value;

    private static Dictionary<string, IpaEntry> BySpelling => SpellingIndex.Value;

    /// <summary>The entry a symbol spells — its own symbol or one of its aliases.</summary>
    public static IpaEntry? Find(string symbol) =>
        BySpelling.TryGetValue(symbol, out var entry) ? entry : null;

    /// <summary>
    /// The trainer family that drills this symbol, or null when none does. Read live from
    /// <see cref="PronunciationCatalog"/> rather than baked into the generated file, so a
    /// family's target sounds and the alphabet's "Practice" links cannot drift apart.
    /// Only a sound English uses can link: the trainer drills English words, and its "r"
    /// target means the English approximant ɹ, not the trill that owns the letter r.
    /// </summary>
    public static string? FamilyKeyFor(string symbol)
    {
        var entry = Find(symbol);
        if (entry is null || !entry.InEnglish) return null;

        var spellings = new HashSet<string>(entry.Aliases) { entry.Symbol };
        return PronunciationCatalog.Families
            .FirstOrDefault(f => f.TargetSounds.Any(spellings.Contains))
            ?.Key;
    }

    private static Dictionary<string, IpaEntry> BuildSpellingIndex()
    {
        var index = new Dictionary<string, IpaEntry>();
        foreach (var entry in All)
        {
            index[entry.Symbol] = entry;
            // A symbol's own spelling always wins over another symbol's alias for it.
            foreach (var alias in entry.Aliases)
            {
                index.TryAdd(alias, entry);
            }
        }

        return index;
    }
}
