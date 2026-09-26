using LanguageLab.Application.Services;
using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Api.Endpoints;

public sealed record FamilyOverviewDto(string Key, string Title, IReadOnlyList<string> TargetSounds, int Total, int Mastered, FamilyStatus Status);
public sealed record ProgressDto(IReadOnlyList<FamilyOverviewDto> Families);
public sealed record WordDto(string Word, string Ipa, string AudioUs, string AudioUk, PronunciationState State, int Streak);
public sealed record FamilyDetailDto(string Key, string Title, IReadOnlyList<string> TargetSounds, IReadOnlyList<WordDto> Words);
public sealed record NextWordDto(WordDto? Word);
public sealed record AttemptRequest(Accent Accent, string Transcript);
public sealed record AttemptResultDto(PronunciationOutcome Outcome, int Score, PronunciationState State, int Streak, bool FamilyDone);
public sealed record IpaEntryDto(
    string Symbol,
    string Name,
    string Hint,
    string Group,
    bool InEnglish,
    string? ExampleWord,
    string? ExampleLanguage,
    string? ExampleIpa,
    string? SoundAudio,
    string? WordAudio,
    string? FamilyKey,
    IReadOnlyList<string> Aliases);
public sealed record IpaSectionDto(string Key, string Title, string Note, IReadOnlyList<IpaEntryDto> Entries);
public sealed record IpaAlphabetDto(IReadOnlyList<IpaSectionDto> Sections);

/// <summary>
/// Thin layer over PronunciationProgressService: the current user from the cookie, DTO
/// mapping, and status codes. No learning logic.
/// </summary>
public static class PronunciationEndpoints
{
    public static void MapPronunciationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/pronunciation").RequireAuthorization();

        group.MapGet("/progress", async (PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var view = await service.GetOverviewAsync(userId);
            return Results.Ok(new ProgressDto(view.Families.Select(ToDto).ToList()));
        });

        // The alphabet is a reference chart, not practice: it reads no user state, and
        // unlike the rest of the mode it needs no speech recognition to be of use.
        group.MapGet("/alphabet", () => Results.Ok(Alphabet));

        group.MapGet("/families/{key}", async (string key, PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var view = await service.GetFamilyAsync(userId, key);
            return view is null ? Results.NotFound() : Results.Ok(ToDto(view));
        });

        group.MapGet("/families/{key}/next", async (string key, PronunciationProgressService service, ICurrentUserContext currentUser, bool includeMastered = false) =>
        {
            var (userId, _) = currentUser.Require();
            var (exists, word) = await service.NextWordAsync(userId, key, includeMastered);
            return exists ? Results.Ok(new NextWordDto(word is null ? null : ToDto(word))) : Results.NotFound();
        });

        group.MapDelete("/words/{word}/progress", async (string word, PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            return await service.ResetWordAsync(userId, word) ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/words/{word}/attempts", async (string word, AttemptRequest request, PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var result = await service.RecordAttemptAsync(userId, word, request.Accent, request.Transcript, DateTime.UtcNow);
            return result is null ? Results.NotFound() : Results.Ok(ToDto(result));
        });
    }

    /// <summary>
    /// Built once: the chart is the same for every user and every request, so there is
    /// nothing to recompute per call.
    /// </summary>
    internal static readonly IpaAlphabetDto Alphabet = new(
        IpaCatalog.Sections.Select(ToDto).ToList());

    private static IpaSectionDto ToDto(IpaSection section) =>
        new(section.Key, section.Title, section.Note, section.Entries.Select(ToDto).ToList());

    private static IpaEntryDto ToDto(IpaEntry entry) =>
        new(
            entry.Symbol,
            entry.Name,
            entry.Hint,
            entry.Group,
            entry.InEnglish,
            entry.ExampleWord,
            entry.ExampleLanguage,
            entry.ExampleIpa,
            PronunciationAudio.Url(entry.SoundAudioFile),
            PronunciationAudio.Url(entry.WordAudioFile),
            IpaCatalog.FamilyKeyFor(entry.Symbol),
            entry.Aliases);

    private static FamilyOverviewDto ToDto(FamilyOverview f) =>
        new(f.Key, f.Title, f.TargetSounds, f.Total, f.Mastered, f.Status);

    private static WordDto ToDto(WordView w) =>
        new(w.Word, w.Ipa, w.AudioUs, w.AudioUk, w.State, w.Streak);

    private static FamilyDetailDto ToDto(FamilyDetailView f) =>
        new(f.Key, f.Title, f.TargetSounds, f.Words.Select(ToDto).ToList());

    private static AttemptResultDto ToDto(AttemptResult r) =>
        new(r.Outcome, r.Score, r.State, r.Streak, r.FamilyDone);
}
