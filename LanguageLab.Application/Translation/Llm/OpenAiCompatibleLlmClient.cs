using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// An OpenAI-compatible chat-completions endpoint — DeepSeek by default. JSON mode
/// (<c>response_format: json_object</c>) guarantees JSON but not a schema, so the request's schema
/// goes into the system message as text; the consumer checks the fields it reads. No retries.
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private const string Name = nameof(LlmProvider.OpenAiCompatible);

    private readonly HttpClient _http;
    private readonly OpenAiCompatibleOptions _options;
    private readonly ILogger<OpenAiCompatibleLlmClient> _logger;

    public OpenAiCompatibleLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OpenAiCompatibleLlmClient> logger)
    {
        _http = http;
        _options = options.Value.OpenAi;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// <see cref="HttpClient.BaseAddress"/> for a configured base URL. The trailing slash matters:
    /// without it "https://host/v1" + "chat/completions" resolves to "https://host/chat/completions".
    /// </summary>
    public static Uri BaseAddressFor(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    public async Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
        {
            // Not logged per call: LlmStartupCheck says it once, at startup.
            throw new LlmUnavailableException($"{Name} is not configured.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = LlmHttp.JsonBody(Body(request)),
        };
        // Trimmed: a key mounted from a file or env var commonly carries a trailing newline, and an
        // untrimmed one would make AuthenticationHeaderValue throw a raw FormatException.
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey?.Trim());

        var response = await LlmHttp.SendAsync(_http, message, Name, _logger, cancellationToken);

        if (response.Status == HttpStatusCode.TooManyRequests)
        {
            throw LlmHttp.Quota(_logger, Name, response.Status, LlmHttp.RetryAfter(response.RetryAfter));
        }

        // DeepSeek answers 402 when the account's balance is spent: a quota, with no hint of when.
        if (response.Status == HttpStatusCode.PaymentRequired)
        {
            throw LlmHttp.Quota(_logger, Name, response.Status, retryAfter: null);
        }

        if (!response.IsSuccess)
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} answered {(int)response.Status}.");
        }

        using var envelope = LlmHttp.ParseEnvelope(response.Body, Name, _logger);

        return LlmHttp.ParseAnswer(AnswerText(envelope.RootElement), Name, _logger);
    }

    private JsonObject Body(LlmRequest request) => new()
    {
        ["model"] = _options.Model,
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = SystemMessage(request) },
            new JsonObject { ["role"] = "user", ["content"] = request.UserContent }),
        ["response_format"] = new JsonObject { ["type"] = "json_object" },
        ["max_tokens"] = request.MaxOutputTokens,
        ["temperature"] = 0,
    };

    /// <summary>
    /// Our instruction, then the schema as compact JSON. The added sentence always says "JSON",
    /// which DeepSeek's JSON mode requires somewhere in the prompt.
    /// </summary>
    private static string SystemMessage(LlmRequest request) =>
        $"{request.SystemInstruction}\n\nAnswer with a single JSON object that matches this JSON Schema:\n{JsonSerializer.Serialize(request.ResponseSchema)}";

    private string AnswerText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || choices[0].ValueKind != JsonValueKind.Object)
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} answered without a choice.");
        }

        var choice = choices[0];
        var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? LlmHttp.StringOrNull(finish) : null;

        if (finishReason != "stop")
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} finish reason {LlmHttp.ReasonCode(finishReason)}.");
        }

        return choice.TryGetProperty("message", out var message)
               && message.ValueKind == JsonValueKind.Object
               && message.TryGetProperty("content", out var content)
            ? LlmHttp.StringOrNull(content) ?? ""
            : "";
    }
}
