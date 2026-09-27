using System.Text.Json;
using LanguageLab.Application.Translation;

namespace LanguageLab.Tests.Fakes;

/// <summary>
/// Answers from <see cref="Answers"/> in order; a queued entry in <see cref="Failures"/> is thrown
/// first. Records every request so a test can inspect the prompt it built.
/// </summary>
public sealed class FakeLlmClient : ILlmClient
{
    public bool IsConfigured { get; set; } = true;
    public Queue<JsonElement> Answers { get; } = new();
    public Queue<LlmException> Failures { get; } = new();
    public List<LlmRequest> Requests { get; } = [];

    public Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfigured)
            throw new LlmUnavailableException("Not configured.");

        Requests.Add(request);
        if (Failures.TryDequeue(out var failure))
            throw failure;
        if (!Answers.TryDequeue(out var answer))
            throw new InvalidOperationException("FakeLlmClient has no answer queued.");
        return Task.FromResult(answer);
    }
}
