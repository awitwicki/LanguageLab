using LanguageLab.Domain.Languages;

namespace LanguageLab.Api.Endpoints;

public sealed record LanguageView(string Code, string EnglishName, string NativeName);

public static class LanguageEndpoints
{
    /// <summary>Anonymous: the catalog is public, and the picker may render before /me settles.</summary>
    public static void MapLanguageEndpoints(this WebApplication app) =>
        app.MapGet("/api/languages", () => Results.Ok(
            LearnerLanguages.All.Select(l => new LanguageView(l.Code, l.EnglishName, l.NativeName))));
}
