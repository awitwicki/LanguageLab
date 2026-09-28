using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// The one <see cref="ILlmClient"/> in the container: every call, and <see cref="IsConfigured"/>,
/// goes to the client <c>Translation:Provider</c> names.
/// </summary>
public sealed class LlmClientSelector : ILlmClient
{
    private readonly ILlmClient _selected;

    public LlmClientSelector(GeminiLlmClient gemini, OpenAiCompatibleLlmClient openAi, IOptions<LlmOptions> options)
    {
        _selected = options.Value.Provider switch
        {
            LlmProvider.Gemini => gemini,
            LlmProvider.OpenAiCompatible => openAi,
            // Unreachable once AddLlmClient's startup validation has run.
            var other => throw new InvalidOperationException($"Unknown translation provider {other}."),
        };
    }

    public bool IsConfigured => _selected.IsConfigured;

    public Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken) =>
        _selected.CompleteJsonAsync(request, cancellationToken);
}
