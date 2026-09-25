using LanguageLab.Application.Translation;
using LanguageLab.Domain;
using Microsoft.AspNetCore.RateLimiting;

namespace LanguageLab.Api.Endpoints;

public sealed record SentenceRequest(string? Text);

public sealed record SentenceTranslationResponse(string Translation);

/// <summary>Reason is "quota" when the provider's monthly quota is gone.</summary>
public sealed record SentenceTranslationError(string Reason);

public static class TranslationEndpoints
{
    public const int MaxSentenceLength = 1000;

    public static void MapTranslationEndpoints(this WebApplication app)
    {
        // A suggestion for the personal dictionary's add form and the reader's word panel. The
        // word is normalized here the same way the dictionary will store it, so a hit in the
        // shared vocabulary is exact.
        app.MapGet("/api/translate", async (
            string? word, TranslationService translation, CancellationToken cancellationToken) =>
        {
            var normalized = WordText.Normalize(word ?? string.Empty);

            if (!WordText.IsValid(normalized))
            {
                return Results.BadRequest();
            }

            return Results.Ok(await translation.LookupAsync(normalized, cancellationToken));
        }).RequireAuthorization()
          .RequireRateLimiting(UserRateLimits.Translate);

        app.MapPost("/api/translate/sentence", TranslateSentenceAsync).RequireAuthorization();
    }

    /// <summary>
    /// The reader's sentence translation. 404 when no translator is configured (never, with
    /// FallbackSentenceTranslator), 400 for empty or over-long text, 429 past the user's daily
    /// characters, 503 { reason: "quota" } when DeepL's monthly quota is gone, 413 when the
    /// provider cannot take a sentence this long, 502 for any other provider failure. Nothing is
    /// stored.
    /// </summary>
    public static async Task<IResult> TranslateSentenceAsync(
        SentenceRequest request,
        ISentenceTranslator translator,
        SentenceQuota quota,
        ICurrentUser currentUser,
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

        if (!quota.TryConsume(await currentUser.GetIdAsync(), text.Length))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var result = await translator.TranslateAsync(text, cancellationToken);

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
