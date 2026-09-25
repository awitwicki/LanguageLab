namespace LanguageLab.Domain.Entities;

/// <summary>
/// Where a WordPair's translation came from. Manual (the default, and every row that existed
/// before this column): imported or typed by a person. Machine: filled in by the translation
/// provider on a lookup. A Manual translation is never replaced by a Machine one.
/// </summary>
public enum TranslationOrigin
{
    Manual = 0,
    Machine = 1,
}
