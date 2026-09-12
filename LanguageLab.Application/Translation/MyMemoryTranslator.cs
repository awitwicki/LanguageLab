using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation;

/// <summary>
/// MyMemory (api.mymemory.translated.net): keyless and documented, 5 000 chars a day per IP.
/// Every failure mode — HTTP error, timeout, a dropped connection mid-transfer, malformed body,
/// exhausted quota, an untranslated echo of the input, or anything else that can go wrong
/// talking to a third party over the network — becomes null plus a warning in the log.
/// <see cref="TranslateAsync"/> never throws: the caller's fallback is the user typing the
/// translation themselves, and no provider hiccup should turn into a 500.
/// </summary>
public sealed class MyMemoryTranslator : ITranslator
{
    public const string BaseUrl = "https://api.mymemory.translated.net/";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly TranslationOptions _options;
    private readonly ILogger<MyMemoryTranslator> _logger;

    public MyMemoryTranslator(HttpClient http, IOptions<TranslationOptions> options, ILogger<MyMemoryTranslator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> TranslateAsync(string word, CancellationToken cancellationToken)
    {
        var query = $"get?q={Uri.EscapeDataString(word)}&langpair=en%7Cuk";

        if (!string.IsNullOrWhiteSpace(_options.MyMemoryEmail))
        {
            query += $"&de={Uri.EscapeDataString(_options.MyMemoryEmail)}";
        }

        try
        {
            using var response = await _http.GetAsync(query, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MyMemory answered {Status} for '{Word}'.", (int)response.StatusCode, word);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Parse(json.RootElement, word);
        }
        catch (Exception e)
        {
            // Deliberately unfiltered: HttpRequestException and TaskCanceledException are the
            // common cases, but a connection dropped mid-transfer can surface as HttpIOException
            // or a plain IOException while reading the response stream, and cancellation can in
            // principle surface as a bare OperationCanceledException. None of that is worth a
            // 500 to the caller — every path here already logs and returns null.
            _logger.LogWarning(e, "MyMemory lookup failed for '{Word}'.", word);
            return null;
        }
    }

    /// <summary>
    /// The body reports its own status: an exhausted quota is an HTTP 200 with responseStatus
    /// 403 (a number or, at times, a string) and a shouting warning in translatedText.
    /// </summary>
    private string? Parse(JsonElement root, string word)
    {
        if (!root.TryGetProperty("responseStatus", out var status) || !IsOk(status))
        {
            _logger.LogWarning("MyMemory refused '{Word}': responseStatus {Status}.", word, status.ToString());
            return null;
        }

        if (!root.TryGetProperty("responseData", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("translatedText", out var text)
            || text.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var translation = text.GetString()!.Trim();

        if (translation.Length == 0
            || translation.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(translation, word, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return translation;
    }

    private static bool IsOk(JsonElement status) =>
        status.ValueKind switch
        {
            JsonValueKind.Number => status.TryGetInt32(out var code) && code == 200,
            JsonValueKind.String => status.GetString() == "200",
            _ => false,
        };
}
