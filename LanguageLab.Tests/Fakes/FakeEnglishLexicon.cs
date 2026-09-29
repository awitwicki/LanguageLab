using LanguageLab.Domain.Lexicon;

namespace LanguageLab.Tests.Fakes;

/// <summary>A lexicon from a form → lemmas table (primary first), case-sensitive like the real one.</summary>
public sealed class FakeEnglishLexicon : IEnglishLexicon
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _formToLemmas;

    public FakeEnglishLexicon(IReadOnlyDictionary<string, IReadOnlyList<string>> formToLemmas) =>
        _formToLemmas = formToLemmas;

    /// <summary>The W0 shape, one lemma per form — kept so tests written against it still compile.</summary>
    public FakeEnglishLexicon(IReadOnlyDictionary<string, string> formToLemma)
        : this(formToLemma.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)new[] { pair.Value },
            StringComparer.Ordinal))
    {
    }

    public IReadOnlyList<string> LemmasOf(string lowercaseForm) =>
        _formToLemmas.TryGetValue(lowercaseForm, out var lemmas) ? lemmas : Array.Empty<string>();

    public string? LemmaOf(string lowercaseForm)
    {
        var lemmas = LemmasOf(lowercaseForm);
        return lemmas.Count > 0 ? lemmas[0] : null;
    }
}
