using LanguageLab.Application.Translation;
using LanguageLab.Domain;

namespace LanguageLab.Api.Endpoints;

public static class TranslationEndpoints
{
    public static void MapTranslationEndpoints(this WebApplication app)
    {
        // A suggestion for the personal dictionary's add form. The word is normalized here the
        // same way the dictionary will store it, so a hit in the shared vocabulary is exact.
        app.MapGet("/api/translate", async (
            string? word, TranslationService translation, CancellationToken cancellationToken) =>
        {
            var normalized = WordText.Normalize(word ?? string.Empty);

            if (!WordText.IsValid(normalized))
            {
                return Results.BadRequest();
            }

            return Results.Ok(await translation.LookupAsync(normalized, cancellationToken));
        }).RequireAuthorization();
    }
}
