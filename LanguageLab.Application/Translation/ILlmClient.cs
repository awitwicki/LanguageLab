using System.Text.Json;

namespace LanguageLab.Application.Translation;

/// <summary>
/// A language model behind one JSON-in, JSON-out call — Gemini, or an OpenAI-compatible endpoint
/// such as DeepSeek, picked by <c>Translation:Provider</c>. Throws <see cref="LlmQuotaException"/>
/// or <see cref="LlmUnavailableException"/>, never returns a partial answer. Implementations must
/// not log <see cref="LlmRequest.UserContent"/>: it can be a sentence of someone's book.
/// </summary>
public interface ILlmClient
{
    /// <summary>False without an API key; every call then throws <see cref="LlmUnavailableException"/>.</summary>
    bool IsConfigured { get; }

    Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken);
}
