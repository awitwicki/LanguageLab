namespace LanguageLab.Domain.Training;

/// <summary>
/// A word as one learner sees it: the English side and its translation in their language.
/// Translation is empty when the word has none in that language.
/// </summary>
public sealed record TranslatedWord(long Id, string Word, string Translation);
