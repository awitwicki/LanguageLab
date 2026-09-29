using LanguageLab.Domain.Languages;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// One word at a time, on top of <see cref="IWordBatchTranslator"/> — a single-element batch.
/// Never throws: a provider hiccup, an exhausted quota, or a word too long to be worth asking
/// about all become null, because the caller's fallback is the user typing the translation
/// themselves.
/// </summary>
public sealed class LlmTranslator : ITranslator
{
    /// <summary>
    /// Defense in depth beyond WordText's own 64-character cap on every known caller — cheap
    /// insurance against a future caller that skips that validation.
    /// </summary>
    public const int MaxWordLength = 100;

    private readonly IWordBatchTranslator _batch;
    private readonly ILogger<LlmTranslator> _logger;

    public LlmTranslator(IWordBatchTranslator batch, ILogger<LlmTranslator> logger)
    {
        _batch = batch;
        _logger = logger;
    }

    public async Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken)
    {
        if (word.Length > MaxWordLength)
        {
            return null;
        }

        try
        {
            var result = await _batch.TranslateAsync([word], target, cancellationToken);
            return result.GetValueOrDefault(word);
        }
        catch (LlmException e)
        {
            // Never the word itself: an LlmException's message never carries user content.
            _logger.LogWarning(e, "Word translation via the LLM failed.");
            return null;
        }
    }
}
