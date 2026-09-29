using System.Text.Json;

namespace LanguageLab.Application.Translation;

/// <summary>
/// A language model behind one JSON-in, JSON-out call — Gemini, or an OpenAI-compatible endpoint
/// such as DeepSeek, picked by <c>Translation:Provider</c>. Throws <see cref="LlmQuotaException"/>
/// or <see cref="LlmUnavailableException"/>, never returns a partial answer. Implementations must
/// not log <see cref="LlmRequest.UserContent"/> — it can be a sentence of someone's book — nor put
/// it, or a provider error body that may echo it, into an exception message.
/// </summary>
public interface ILlmClient
{
    /// <summary>False without an API key; every call then throws <see cref="LlmUnavailableException"/>.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The returned element must not depend on a <see cref="JsonDocument"/> the implementation
    /// disposes — return <c>RootElement.Clone()</c> when parsing a response into a document that
    /// goes out of scope, or the caller reads a disposed document and throws.
    /// </summary>
    Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken);
}
