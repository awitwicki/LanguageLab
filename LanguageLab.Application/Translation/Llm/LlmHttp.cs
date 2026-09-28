using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>A provider's HTTP answer, read in full: status, body and its Retry-After header.</summary>
internal sealed record LlmHttpResponse(HttpStatusCode Status, string Body, RetryConditionHeaderValue? RetryAfter)
{
    public bool IsSuccess => (int)Status is >= 200 and <= 299;
}

/// <summary>
/// What both LLM clients share: sending, the A1 spec's mapping of failures to
/// <see cref="LlmUnavailableException"/> / <see cref="LlmQuotaException"/> with one warning each,
/// and parsing the model's text into a JSON object. Every message built here names a provider, a
/// status, an exception type or a reason code — never request text, a response body or a key.
/// </summary>
internal static partial class LlmHttp
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest back-off a provider's hint may ask for; a longer one is clamped.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromDays(1);

    public static LlmQuotaException Quota(ILogger logger, string provider, HttpStatusCode status, TimeSpan? retryAfter)
    {
        var message = $"{provider} refused the call for quota ({(int)status}).";
        logger.LogWarning("{LlmFailure}", message);
        return new LlmQuotaException(message, retryAfter);
    }

    /// <summary>A Retry-After header, in seconds or as an HTTP date, as a wait from now.</summary>
    public static TimeSpan? RetryAfter(RetryConditionHeaderValue? header) => header switch
    {
        { Delta: { } delta } => Clamp(delta),
        { Date: { } date } => Clamp(date - DateTimeOffset.UtcNow),
        _ => null,
    };

    /// <summary>A protobuf Duration string such as "37s" or "1.5s"; null for anything else.</summary>
    public static TimeSpan? ParseDuration(string? value)
    {
        if (value is null
            || !value.EndsWith('s')
            || !decimal.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return seconds >= (decimal)MaxRetryAfter.TotalSeconds ? MaxRetryAfter : TimeSpan.FromSeconds((double)seconds);
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > MaxRetryAfter ? MaxRetryAfter : value;

    public static StringContent JsonBody(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public static async Task<LlmHttpResponse> SendAsync(
        HttpClient http, HttpRequestMessage message, string provider, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new LlmHttpResponse(response.StatusCode, body, response.Headers.RetryAfter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            // HttpClient's own timeout surfaces as TaskCanceledException while the caller's token is
            // still live. Only the type is named: an exception message can quote the request.
            throw Unavailable(logger, $"{provider} request failed: {e.GetType().Name}.", e);
        }
    }

    public static LlmUnavailableException Unavailable(ILogger logger, string message, Exception? inner = null)
    {
        logger.LogWarning("{LlmFailure}", message);
        return new LlmUnavailableException(message, inner);
    }

    /// <summary>The provider's response envelope; a body that is not JSON is unavailable.</summary>
    public static JsonDocument ParseEnvelope(string body, string provider, ILogger logger)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw Unavailable(logger, $"{provider} answered a body that is not JSON.");
        }
    }

    /// <summary>
    /// The model's text as a JSON object, cloned so it outlives the document it was parsed into.
    /// Its shape against the request's schema is the consumer's to check.
    /// </summary>
    public static JsonElement ParseAnswer(string text, string provider, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Unavailable(logger, $"{provider} answered with empty text.");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw Unavailable(logger, $"{provider} answered text that is not JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Unavailable(logger, $"{provider} answered JSON that is not an object.");
            }

            return document.RootElement.Clone();
        }
    }

    public static string? StringOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    /// <summary>
    /// A finish or block reason, fit for a message: a provider's codes (STOP, MAX_TOKENS, length,
    /// content_filter) pass; anything else is response text and is not quoted.
    /// </summary>
    public static string ReasonCode(string? value) =>
        value is null ? "missing" : ReasonCodePattern().IsMatch(value) ? value : "unrecognised";

    [GeneratedRegex("^[A-Za-z_]{1,40}$")]
    private static partial Regex ReasonCodePattern();
}
