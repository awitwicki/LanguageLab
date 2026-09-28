using System.Text.Json;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class LlmClientSelectorTests
{
    private const string GeminiAnswer =
        """{"candidates":[{"content":{"parts":[{"text":"{\"from\":\"gemini\"}"}]},"finishReason":"STOP"}]}""";
    private const string OpenAiAnswer =
        """{"choices":[{"message":{"role":"assistant","content":"{\"from\":\"openai\"}"},"finish_reason":"stop"}]}""";

    private static readonly LlmRequest Request =
        new("Translate.", "whale", JsonDocument.Parse("""{"type":"object"}""").RootElement, 64);

    private static (LlmClientSelector Selector, StubHttpHandler Gemini, StubHttpHandler OpenAi) Build(
        LlmProvider provider, string? geminiKey = "gm-key", string? openAiKey = "sk-key")
    {
        var options = Options.Create(new LlmOptions
        {
            Provider = provider,
            Gemini = new GeminiOptions { ApiKey = geminiKey },
            OpenAi = new OpenAiCompatibleOptions { ApiKey = openAiKey },
        });
        var gemini = new StubHttpHandler(_ => StubHttpHandler.Json(GeminiAnswer));
        var openAi = new StubHttpHandler(_ => StubHttpHandler.Json(OpenAiAnswer));
        var selector = new LlmClientSelector(
            new GeminiLlmClient(
                new HttpClient(gemini) { BaseAddress = new Uri(GeminiLlmClient.BaseUrl) },
                options, NullLogger<GeminiLlmClient>.Instance),
            new OpenAiCompatibleLlmClient(
                new HttpClient(openAi) { BaseAddress = new Uri("https://api.deepseek.com/") },
                options, NullLogger<OpenAiCompatibleLlmClient>.Instance),
            options);
        return (selector, gemini, openAi);
    }

    [Theory]
    [InlineData(LlmProvider.Gemini, "gemini")]
    [InlineData(LlmProvider.OpenAiCompatible, "openai")]
    public async Task Sends_every_call_to_the_named_provider(LlmProvider provider, string expected)
    {
        var (selector, gemini, openAi) = Build(provider);

        var answer = await selector.CompleteJsonAsync(Request, CancellationToken.None);

        Assert.Equal(expected, answer.GetProperty("from").GetString());
        Assert.Equal(provider == LlmProvider.Gemini ? 1 : 0, gemini.Calls);
        Assert.Equal(provider == LlmProvider.OpenAiCompatible ? 1 : 0, openAi.Calls);
    }

    [Theory]
    [InlineData(LlmProvider.Gemini, "gm-key", null, true)]
    [InlineData(LlmProvider.Gemini, null, "sk-key", false)]
    [InlineData(LlmProvider.OpenAiCompatible, null, "sk-key", true)]
    [InlineData(LlmProvider.OpenAiCompatible, "gm-key", null, false)]
    public void Is_configured_when_the_named_provider_has_a_key(
        LlmProvider provider, string? geminiKey, string? openAiKey, bool expected)
    {
        Assert.Equal(expected, Build(provider, geminiKey, openAiKey).Selector.IsConfigured);
    }
}
