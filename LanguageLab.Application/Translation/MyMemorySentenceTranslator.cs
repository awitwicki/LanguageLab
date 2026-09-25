using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// Sentence translation through MyMemory — the provider the word lookups already use — for when no
/// DeepL key is configured. MyMemory refuses a query over 500 bytes, so a longer sentence is
/// answered TooLong without a request. Its daily quota (5 000 characters per server IP, 50 000
/// with Translation:MyMemoryEmail) is shared with the word lookups, so sentences are additionally
/// capped by MyMemorySentenceBudget, a server-wide daily share — the budget is asked, and refused,
/// before a request is made. Like the other translators it never throws, and it logs a status or
/// an exception type, never the text.
/// </summary>
public sealed class MyMemorySentenceTranslator : ISentenceTranslator
{
    public const int MaxBytes = 500;

    private readonly HttpClient _http;
    private readonly TranslationOptions _options;
    private readonly MyMemorySentenceBudget _budget;
    private readonly ILogger<MyMemorySentenceTranslator> _logger;

    public MyMemorySentenceTranslator(
        HttpClient http,
        IOptions<TranslationOptions> options,
        MyMemorySentenceBudget budget,
        ILogger<MyMemorySentenceTranslator> logger)
    {
        _http = http;
        _options = options.Value;
        _budget = budget;
        _logger = logger;
    }

    public bool IsConfigured => true;

    public async Task<SentenceTranslation> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            return SentenceTranslation.TooLong;
        }

        if (!_budget.TryConsume(text.Length, DateTime.UtcNow))
        {
            _logger.LogWarning("MyMemory's daily sentence budget is spent.");
            return SentenceTranslation.Quota;
        }

        var query = $"get?q={Uri.EscapeDataString(text)}&langpair=en%7Cuk";

        if (!string.IsNullOrWhiteSpace(_options.MyMemoryEmail))
        {
            query += $"&de={Uri.EscapeDataString(_options.MyMemoryEmail)}";
        }

        try
        {
            using var response = await _http.GetAsync(query, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("MyMemory refused a sentence: too many requests.");
                return SentenceTranslation.Quota;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MyMemory answered {Status} for a sentence.", (int)response.StatusCode);
                return SentenceTranslation.Failure;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Parse(json.RootElement);
        }
        catch (Exception e)
        {
            // Unfiltered for the same reasons as MyMemoryTranslator; the type only, never the text.
            _logger.LogWarning("MyMemory sentence request failed: {Error}.", e.GetType().Name);
            return SentenceTranslation.Failure;
        }
    }

    /// <summary>The body reports its own status: an exhausted quota is responseStatus 403 or quotaFinished.</summary>
    private SentenceTranslation Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SentenceTranslation.Failure;
        }

        var finished = root.TryGetProperty("quotaFinished", out var quotaFinished) && quotaFinished.ValueKind == JsonValueKind.True;
        root.TryGetProperty("responseStatus", out var status);

        if (finished || IsCode(status, 403))
        {
            _logger.LogWarning("MyMemory's daily quota is exhausted.");
            return SentenceTranslation.Quota;
        }

        if (!IsCode(status, 200)
            || !root.TryGetProperty("responseData", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("translatedText", out var translated)
            || translated.ValueKind != JsonValueKind.String)
        {
            return SentenceTranslation.Failure;
        }

        var text = translated.GetString()!.Trim();

        return text.Length == 0 || text.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)
            ? SentenceTranslation.Failure
            : SentenceTranslation.Success(text);
    }

    private static bool IsCode(JsonElement status, int code) =>
        status.ValueKind switch
        {
            JsonValueKind.Number => status.TryGetInt32(out var value) && value == code,
            JsonValueKind.String => status.GetString() == code.ToString(),
            _ => false,
        };
}
