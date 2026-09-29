using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// Google's Gemini API (generateContent) with native structured output: the request's schema goes
/// to the model as <c>responseJsonSchema</c>. The key rides in the <c>x-goog-api-key</c> header,
/// never in the URL, which HttpClient's logging records. No retries — the caller decides.
/// </summary>
public sealed class GeminiLlmClient : ILlmClient
{
    public const string BaseUrl = "https://generativelanguage.googleapis.com/";

    private const string Name = nameof(LlmProvider.Gemini);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiLlmClient> _logger;

    public GeminiLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<GeminiLlmClient> logger)
    {
        _http = http;
        _options = options.Value.Gemini;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<JsonElement> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConfigured)
        {
            // Not logged per call: LlmStartupCheck says it once, at startup.
            throw new LlmUnavailableException($"{Name} is not configured.");
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(_options.Model)}:generateContent")
        {
            Content = LlmHttp.JsonBody(Body(request)),
        };
        // Trimmed: a key mounted from a file or env var commonly carries a trailing newline.
        message.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey?.Trim());

        var response = await LlmHttp.SendAsync(_http, message, Name, _logger, cancellationToken);

        if (response.Status == HttpStatusCode.TooManyRequests)
        {
            throw LlmHttp.Quota(_logger, Name, response.Status,
                LlmHttp.RetryAfter(response.RetryAfter) ?? RetryDelay(response.Body));
        }

        if (!response.IsSuccess)
        {
            throw LlmHttp.UnavailableStatus(_logger, Name, response.Status, response.Body);
        }

        using var envelope = LlmHttp.ParseEnvelope(response.Body, Name, _logger);

        return LlmHttp.ParseAnswer(AnswerText(envelope.RootElement), Name, _logger);
    }

    private static JsonObject Body(LlmRequest request) => new()
    {
        ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(Text(request.SystemInstruction)) },
        ["contents"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(Text(request.UserContent)),
        }),
        ["generationConfig"] = new JsonObject
        {
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = JsonNode.Parse(request.ResponseSchema.GetRawText()),
            ["maxOutputTokens"] = request.MaxOutputTokens,
            ["temperature"] = 0,
            // Gemini 3.x thinks by default, and thinking tokens count against maxOutputTokens.
            ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "minimal" },
        },
    };

    private static JsonObject Text(string text) => new() { ["text"] = text };

    /// <summary>
    /// The RetryInfo detail of a 429 body — the <c>error.details[]</c> entry whose <c>@type</c> ends
    /// in "RetryInfo". Read for <c>retryDelay</c> alone; nothing else of the body is kept.
    /// </summary>
    private static TimeSpan? RetryDelay(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind == JsonValueKind.Object
                    && detail.TryGetProperty("@type", out var type)
                    && LlmHttp.StringOrNull(type)?.EndsWith("RetryInfo", StringComparison.Ordinal) == true
                    && detail.TryGetProperty("retryDelay", out var delay))
                {
                    return LlmHttp.ParseDuration(LlmHttp.StringOrNull(delay));
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The model's text: every non-thought part of the first candidate, joined — Gemini may split
    /// an answer over parts and, with thinking on, put a thought part first.
    /// </summary>
    private string AnswerText(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.ValueKind == JsonValueKind.Object
            && feedback.TryGetProperty("blockReason", out var blockReason))
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} blocked the prompt: {LlmHttp.ReasonCode(LlmHttp.StringOrNull(blockReason))}.");
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0
            || candidates[0].ValueKind != JsonValueKind.Object)
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} answered without a candidate.");
        }

        var candidate = candidates[0];
        var finishReason = candidate.TryGetProperty("finishReason", out var finish) ? LlmHttp.StringOrNull(finish) : null;

        if (finishReason != "STOP")
        {
            throw LlmHttp.Unavailable(_logger, $"{Name} finish reason {LlmHttp.ReasonCode(finishReason)}.");
        }

        var text = new StringBuilder();

        if (candidate.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var isThought = part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;

                if (!isThought && part.TryGetProperty("text", out var partText))
                {
                    text.Append(LlmHttp.StringOrNull(partText));
                }
            }
        }

        return text.ToString();
    }
}
