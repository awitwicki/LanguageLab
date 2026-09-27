using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

/// <summary>
/// Asks for a dictionary's shared words to be translated into a language in the background.
/// Idempotent per (dictionary, language): a request while one is pending or running is a no-op,
/// so callers enqueue freely — on import, and whenever a learner opens a dictionary in a language
/// it is not yet translated into.
/// </summary>
public interface ITranslationQueue
{
    Task EnqueueAsync(int dictionaryId, LearnerLanguage language, CancellationToken cancellationToken);
}
