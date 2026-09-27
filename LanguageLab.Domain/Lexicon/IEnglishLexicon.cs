namespace LanguageLab.Domain.Lexicon;

/// <summary>
/// The English word list both lemmatizer and whitelist are built on: a form maps to its lemma,
/// and a form that is not in the list is not an English word — a book import drops it. Takes the
/// lowercase form; the caller lowercases, so "Went" is not found.
/// </summary>
public interface IEnglishLexicon
{
    string? LemmaOf(string lowercaseForm);
}
