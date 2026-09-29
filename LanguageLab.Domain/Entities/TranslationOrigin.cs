namespace LanguageLab.Domain.Entities;

/// <summary>
/// Where a WordTranslation's text came from. Manual: imported or typed by a person. Machine:
/// filled in by the translation provider on a lookup. A Manual translation is never replaced by
/// a Machine one.
/// </summary>
public enum TranslationOrigin
{
    Manual = 0,
    Machine = 1,
}
