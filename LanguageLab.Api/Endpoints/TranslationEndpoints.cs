using System.Globalization;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Domain;
using Microsoft.AspNetCore.RateLimiting;

namespace LanguageLab.Api.Endpoints;

public sealed record SentenceRequest(string? Text);

public sealed record SentenceTranslationResponse(string Translation);

/// <summary>Reason is "quota" when the provider's quota is gone.</summary>
public sealed record SentenceTranslationError(string Reason);

public static class TranslationEndpoints
{
    public const int MaxSentenceLength = LlmSentenceTranslator.MaxTextLength;

    public static void MapTranslationEndpoints(this WebApplication app)
    {
        // A suggestion for the personal dictionary's add form. The word is normalized here the
        // same way the dictionary will store it, so a hit in the shared vocabulary is exact.
        app.MapGet("/api/translate", TranslateWordAsync)
            .RequireAuthorization()
            .RequireRateLimiting(UserRateLimits.Translate);

        app.MapPost("/api/translate/sentence", TranslateSentenceAsync).RequireAuthorization();
    }

    /// <summary>
    /// 400 for a word the dictionary could not store; 429 with Retry-After for a miss inside the
    /// user's uncached-translation window (a hit is never limited); otherwise the lookup.
    /// </summary>
    public static async Task<IResult> TranslateWordAsync(
        string? word,
        TranslationService translation,
        ICurrentUser currentUser,
        ICurrentLanguage language,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var normalized = WordText.Normalize(word ?? string.Empty);

        if (!WordText.IsValid(normalized))
        {
            return Results.BadRequest();
        }

        var lookup = await translation.LookupAsync(
            await currentUser.GetIdAsync(), normalized, language.Require(), cancellationToken);

        return lookup is { Source: TranslationSource.RateLimited, RetryAfterSeconds: { } seconds }
            ? TooManyRequests(http, seconds)
            : Results.Ok(lookup);
    }

    /// <summary>A bare 429 that names its wait, like UserRateLimits' own rejections.</summary>
    internal static IResult TooManyRequests(HttpContext http, int retryAfterSeconds)
    {
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// The reader's sentence translation. 404 when no language model is configured, 400 for empty
    /// or over-long text, 429 with Retry-After inside the user's uncached-translation window (every
    /// sentence reaches the model, so every one spends the slot), 503 { reason: "quota" } when the
    /// provider's quota is gone, 413 when it cannot take a text this long, 502 for any other
    /// provider failure. Nothing is stored.
    /// </summary>
    public static async Task<IResult> TranslateSentenceAsync(
        SentenceRequest request,
        ISentenceTranslator translator,
        UncachedTranslationLimiter limiter,
        ICurrentUser currentUser,
        ICurrentLanguage language,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!translator.IsConfigured)
        {
            return Results.NotFound();
        }

        var text = (request.Text ?? string.Empty).Trim();

        if (text.Length is 0 or > MaxSentenceLength)
        {
            return Results.BadRequest();
        }

        var target = language.Require();

        if (!limiter.TryConsume(await currentUser.GetIdAsync(), out var wait))
        {
            return TooManyRequests(http, UncachedTranslationLimiter.Seconds(wait));
        }

        var result = await translator.TranslateAsync(text, target, cancellationToken);

        return result.Status switch
        {
            SentenceTranslationStatus.Ok => Results.Ok(new SentenceTranslationResponse(result.Text!)),
            SentenceTranslationStatus.QuotaExceeded => Results.Json(
                new SentenceTranslationError("quota"), statusCode: StatusCodes.Status503ServiceUnavailable),
            SentenceTranslationStatus.TooLong => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
            _ => Results.StatusCode(StatusCodes.Status502BadGateway),
        };
    }
}
