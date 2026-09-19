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

        group.MapGet("/families/{key}", async (string key, PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var view = await service.GetFamilyAsync(userId, key);
            return view is null ? Results.NotFound() : Results.Ok(ToDto(view));
        });

        group.MapGet("/families/{key}/next", async (string key, PronunciationProgressService service, ICurrentUserContext currentUser, bool includeMastered = false) =>
        {
            var (userId, _) = currentUser.Require();
            var (available, word) = await service.NextWordAsync(userId, key, includeMastered);
            return available ? Results.Ok(new NextWordDto(word is null ? null : ToDto(word))) : Results.NotFound();
        });

        group.MapPost("/words/{word}/attempts", async (string word, AttemptRequest request, PronunciationProgressService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var result = await service.RecordAttemptAsync(userId, word, request.Accent, request.Transcript, DateTime.UtcNow);
            return result is null ? Results.NotFound() : Results.Ok(ToDto(result));
        });
    }

    private static FamilyOverviewDto ToDto(FamilyOverview f) =>
        new(f.Key, f.Title, f.TargetSounds, f.Total, f.Mastered, f.Status);

    private static WordDto ToDto(WordView w) =>
        new(w.Word, w.Ipa, w.AudioUs, w.AudioUk, w.State, w.Streak);

    private static FamilyDetailDto ToDto(FamilyDetailView f) =>
        new(f.Key, f.Title, f.TargetSounds, f.Words.Select(ToDto).ToList());

    private static AttemptResultDto ToDto(AttemptResult r) =>
        new(r.Outcome, r.Score, r.State, r.Streak, r.FamilyDone);
}
