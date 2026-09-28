using System.Text;

namespace LanguageLab.Domain.Lexicon;

/// <summary>
/// <see cref="IEnglishLexicon"/> over english-lexicon.txt, embedded in this assembly — generated
/// by scripts/build_lexicon.py from SCOWL and AGID, never edited by hand. One line per form: a
/// lone word is a lemma of itself only; otherwise the form, then its lemmas, primary first. The
/// table is read on first use, once, however many threads ask at the same time.
/// </summary>
public sealed class EnglishLexicon : IEnglishLexicon
{
    public const string ResourceName = "LanguageLab.Domain.Lexicon.english-lexicon.txt";

    private readonly Lazy<Dictionary<string, string[]>> _table =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<string> LemmasOf(string lowercaseForm) =>
        _table.Value.TryGetValue(lowercaseForm, out var lemmas) ? lemmas : Array.Empty<string>();

    public string? LemmaOf(string lowercaseForm)
    {
        var lemmas = LemmasOf(lowercaseForm);
        return lemmas.Count > 0 ? lemmas[0] : null;
    }

    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(EnglishLexicon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;

            var words = line.Split(' ');
            table[words[0]] = words.Length == 1 ? words : words[1..];
        }

        return table;
    }
}
