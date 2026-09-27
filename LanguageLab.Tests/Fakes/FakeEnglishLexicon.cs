using LanguageLab.Domain.Lexicon;

namespace LanguageLab.Tests.Fakes;

/// <summary>A lexicon from a form → lemma table, case-sensitive like the real one.</summary>
public sealed class FakeEnglishLexicon : IEnglishLexicon
{
    private readonly IReadOnlyDictionary<string, string> _formToLemma;

    public FakeEnglishLexicon(IReadOnlyDictionary<string, string> formToLemma) => _formToLemma = formToLemma;

    public string? LemmaOf(string lowercaseForm) =>
        _formToLemma.TryGetValue(lowercaseForm, out var lemma) ? lemma : null;
}
