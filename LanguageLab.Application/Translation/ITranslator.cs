using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

/// <summary>
/// English → the learner's language (target), one word or phrase at a time. Null means "no
/// translation" — provider trouble (network, quota, nonsense) never throws, because the caller's
/// fallback is the user typing the translation themselves.
/// </summary>
public interface ITranslator
{
    Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken);
}
