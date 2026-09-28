using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation.Queue;

/// <summary>
/// The word batch translator until a real one is registered: never configured, so the
/// translation worker idles and the app still starts. Registered with TryAdd — a real
/// IWordBatchTranslator registered before or after AddTranslationQueue() wins.
/// </summary>
public sealed class NullWordBatchTranslator : IWordBatchTranslator
{
    public bool IsConfigured => false;

    public Task<IReadOnlyDictionary<string, string>> TranslateAsync(
        IReadOnlyList<string> lemmas, LearnerLanguage target, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyDictionary<string, string>>(
            new LlmUnavailableException("No word batch translator is configured."));
}
