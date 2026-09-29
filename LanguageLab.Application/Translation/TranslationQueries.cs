using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;

namespace LanguageLab.Application.Translation;

/// <summary>
/// The one place a word's translation in a language is read. "Translated" means a
/// WordTranslation row in that language; there is no empty-string convention any more.
/// </summary>
public static class TranslationQueries
{
    public static IQueryable<WordPair> TranslatedInto(this IQueryable<WordPair> words, string language) =>
        words.Where(w => w.Translations.Any(t => t.Language == language));

    /// <summary>A final projection: filter and order before it, not after.</summary>
    public static IQueryable<TranslatedWord> Translated(this IQueryable<WordPair> words, string language) =>
        words.Select(w => new TranslatedWord(
            w.Id,
            w.Word,
            w.Translations.Where(t => t.Language == language).Select(t => t.Text).FirstOrDefault() ?? ""));
}
