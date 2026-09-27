using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

/// <summary>
/// English lemmas → the learner's language, many at a time — what the background dictionary
/// translation and the single-word lookup are both built on. The answer holds only lemmas that
/// were asked for; a lemma missing from it got no usable translation. Unlike
/// <see cref="ITranslator"/> it throws <see cref="LlmQuotaException"/> and
/// <see cref="LlmUnavailableException"/>, because a queue has to tell "no answer for this word"
/// from "try the whole batch later".
/// </summary>
public interface IWordBatchTranslator
{
    bool IsConfigured { get; }

    Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken cancellationToken);
}
