using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

/// <summary>
/// English lemmas → the learner's language, many at a time — what the background dictionary
/// translation and the single-word lookup are both built on. The answer holds only lemmas that
/// were asked for, once each even if a lemma was requested twice; a lemma missing from it got no
/// usable translation. Unlike <see cref="ITranslator"/> it throws <see cref="LlmQuotaException"/>
/// and <see cref="LlmUnavailableException"/>, because a queue has to tell "no answer for this
/// word" from "try the whole batch later" — a call that throws has translated none of its lemmas;
/// there is no partial answer on failure, so a caller retries the whole batch.
/// <see cref="TranslateAsync"/> makes one provider call per invocation and does not split
/// internally — callers keep one call to a provider-appropriate size (the roadmap's background
/// worker uses batches of about 200).
/// </summary>
public interface IWordBatchTranslator
{
    /// <summary>False without credentials configured; every call then throws <see cref="LlmUnavailableException"/>.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken cancellationToken);
}
