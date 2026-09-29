namespace LanguageLab.Domain.Lexicon;

/// <summary>
/// The English word list both lemmatizer and whitelist are built on: a form maps to its lemmas,
/// and a form that is not in the list is not an English word — a book import drops it. Takes the
/// lowercase form; the caller lowercases, so "Went" is not found.
/// </summary>
public interface IEnglishLexicon
{
    /// <summary>All lemmas of the form, primary first; empty when the form is not an English word.</summary>
    IReadOnlyList<string> LemmasOf(string lowercaseForm);

    /// <summary>The primary lemma — the first of <see cref="LemmasOf"/> — or null when the form is not an English word.</summary>
    string? LemmaOf(string lowercaseForm);
}
