using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// DeepL API Free (api-free.deepl.com): 500 000 characters a month. Like MyMemoryTranslator,
/// every failure becomes a result plus a warning in the log, never an exception — and the
/// warning names a status or an exception type, never the text being translated.
/// </summary>
public sealed class DeepLTranslator : ISentenceTranslator
{
    public const string BaseUrl = "https://api-free.deepl.com/";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>DeepL's own status for "character quota exceeded".</summary>
    private const int QuotaExceededStatus = 456;

    private readonly HttpClient _http;
    private readonly TranslationOptions _options;
    private readonly ILogger<DeepLTranslator> _logger;

    public DeepLTranslator(HttpClient http, IOptions<TranslationOptions> options, ILogger<DeepLTranslator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.DeepLApiKey);

    public async Task<SentenceTranslation> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return SentenceTranslation.Failure;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/translate")
        {
            Content = JsonContent.Create(new DeepLRequest([text], "EN", "UK")),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_options.DeepLApiKey}");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode == QuotaExceededStatus)
            {
                _logger.LogWarning("DeepL's character quota is exhausted.");
                return SentenceTranslation.Quota;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DeepL answered {Status}.", (int)response.StatusCode);
                return SentenceTranslation.Failure;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var translated = Parse(json.RootElement);

            if (translated == null)
            {
                _logger.LogWarning("DeepL answered without a translation.");
                return SentenceTranslation.Failure;
            }

            return SentenceTranslation.Success(translated);
        }
        catch (Exception e)
        {
            // Unfiltered for the same reasons as MyMemoryTranslator. Only the type is logged:
            // an exception message could quote the request, and the request is someone's book.
            _logger.LogWarning("DeepL request failed: {Error}.", e.GetType().Name);
            return SentenceTranslation.Failure;
        }
    }

    private static string? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("translations", out var translations)
            || translations.ValueKind != JsonValueKind.Array
            || translations.GetArrayLength() == 0
            || !translations[0].TryGetProperty("text", out var text)
            || text.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var translated = text.GetString()!.Trim();

        return translated.Length == 0 ? null : translated;
    }

    private sealed record DeepLRequest(
        [property: JsonPropertyName("text")] string[] Text,
        [property: JsonPropertyName("source_lang")] string SourceLang,
        [property: JsonPropertyName("target_lang")] string TargetLang);
}
