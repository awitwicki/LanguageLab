using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

/// <summary>
/// Asks for a dictionary's shared words to be translated into a language in the background.
/// Idempotent per (dictionary, language): a request while the job is pending is a no-op, and a
/// request after it completed or failed starts a fresh pass — so callers enqueue freely, on import
/// and whenever a learner opens a dictionary in a language it is not yet translated into. A
/// personal dictionary, or one that does not exist, is ignored.
/// The implementation is scoped and saves on the caller's <c>ApplicationDbContext</c>: call it
/// after your own <c>SaveChangesAsync</c>, never from inside an open transaction, since its own
/// save (and the unique-index retry it catches) would then also flush and could swallow the
/// caller's unrelated changes.
/// </summary>
public interface ITranslationQueue
{
    Task EnqueueAsync(long dictionaryId, LearnerLanguage language, CancellationToken cancellationToken);
}
