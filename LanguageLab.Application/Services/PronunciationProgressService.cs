using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Pronunciation;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record FamilyOverview(string Key, string Title, IReadOnlyList<string> TargetSounds, int Total, int Mastered, FamilyStatus Status);
public sealed record PronunciationProgressView(IReadOnlyList<FamilyOverview> Families);
public sealed record WordView(string Word, string Ipa, string AudioUs, string AudioUk, PronunciationState State, int Streak);
public sealed record FamilyDetailView(string Key, string Title, IReadOnlyList<string> TargetSounds, IReadOnlyList<WordView> Words);
public sealed record AttemptResult(PronunciationOutcome Outcome, int Score, PronunciationState State, int Streak, bool FamilyDone);

/// <summary>
/// Per-user standing on the pronunciation catalog: family lock/unlock, per-word state,
/// picking the next word to practice, and grading an attempt. Nothing is stored beyond
/// PronunciationProgress/PronunciationAttempt — family status is recomputed every call,
/// same as the verb trainer's LearningPath.
/// </summary>
public class PronunciationProgressService
{
    private readonly ApplicationDbContext _dbContext;

    public PronunciationProgressService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PronunciationProgressView> GetOverviewAsync(long userId)
    {
        var path = PronunciationLearningPath.Evaluate(await StatesAsync(userId));

        var families = path
            .Select(f => new FamilyOverview(f.Family.Key, f.Family.Title, f.Family.TargetSounds, f.Total, f.Mastered, f.Status))
            .ToList();

        return new PronunciationProgressView(families);
    }

    public async Task<FamilyDetailView?> GetFamilyAsync(long userId, string familyKey)
    {
        var status = await FamilyStatusAsync(userId, familyKey);
        if (status is null or FamilyStatus.Locked)
        {
            return null;
        }

        var family = PronunciationCatalog.FamilyByKey(familyKey)!;
        var catalogWords = PronunciationCatalog.WordsOf(familyKey);
        var progress = await ProgressByWordAsync(userId, catalogWords.Select(w => w.Word));

        var words = catalogWords.Select(w => ToWordView(w, progress)).ToList();
        return new FamilyDetailView(family.Key, family.Title, family.TargetSounds, words);
    }

    public async Task<(bool FamilyAvailable, WordView? Word)> NextWordAsync(long userId, string familyKey, bool includeMastered)
    {
        var status = await FamilyStatusAsync(userId, familyKey);
        if (status is null or FamilyStatus.Locked)
        {
            return (false, null);
        }

        var catalogWords = PronunciationCatalog.WordsOf(familyKey);
        var progress = await ProgressByWordAsync(userId, catalogWords.Select(w => w.Word));

        var next = catalogWords.FirstOrDefault(w => !progress.ContainsKey(w.Word))
            ?? catalogWords
                .Where(w => progress.TryGetValue(w.Word, out var p) && p.State == PronunciationState.Learning)
                .OrderBy(w => progress[w.Word].LastSeenAt)
                .FirstOrDefault()
            ?? (includeMastered
                ? catalogWords
                    .Where(w => progress.TryGetValue(w.Word, out var p) && p.State == PronunciationState.Mastered)
                    .OrderBy(w => progress[w.Word].LastSeenAt)
                    .FirstOrDefault()
                : null);

        return (true, next is null ? null : ToWordView(next, progress));
    }

    public async Task<AttemptResult?> RecordAttemptAsync(long userId, string word, Accent accent, string transcript, DateTime nowUtc)
    {
        var catalogWord = PronunciationCatalog.Find(word);
        if (catalogWord is null)
        {
            return null;
        }

        var (score, outcome) = PronunciationAnswerChecker.Check(catalogWord.Word, transcript);

        var progress = await _dbContext.PronunciationProgresses
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Word == catalogWord.Word);

        if (progress is null)
        {
            progress = new PronunciationProgress { UserId = userId, Word = catalogWord.Word, LastSeenAt = nowUtc };
            _dbContext.PronunciationProgresses.Add(progress);
        }

        var next = PronunciationStateMachine.Apply(new ProgressState(progress.State, progress.Streak), outcome == PronunciationOutcome.Correct);
        progress.State = next.State;
        progress.Streak = next.Streak;
        progress.LastSeenAt = nowUtc;

        _dbContext.PronunciationAttempts.Add(new PronunciationAttempt
        {
            UserId = userId,
            Word = catalogWord.Word,
            Accent = accent,
            Transcript = transcript,
            Score = score,
            Outcome = outcome,
            CreatedAt = nowUtc,
        });

        await _dbContext.SaveChangesAsync();

        var familyDone = PronunciationLearningPath.Evaluate(await StatesAsync(userId))
            .First(f => f.Family.Key == catalogWord.FamilyKey).Status == FamilyStatus.Done;

        return new AttemptResult(outcome, score, progress.State, progress.Streak, familyDone);
    }

    private async Task<FamilyStatus?> FamilyStatusAsync(long userId, string familyKey)
    {
        var match = PronunciationLearningPath.Evaluate(await StatesAsync(userId))
            .FirstOrDefault(f => f.Family.Key == familyKey);
        return match?.Status;
    }

    private async Task<Dictionary<string, PronunciationState>> StatesAsync(long userId) =>
        await _dbContext.PronunciationProgresses
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Word, p => p.State);

    private async Task<Dictionary<string, PronunciationProgress>> ProgressByWordAsync(long userId, IEnumerable<string> words)
    {
        var set = words.ToHashSet();
        return await _dbContext.PronunciationProgresses
            .Where(p => p.UserId == userId && set.Contains(p.Word))
            .ToDictionaryAsync(p => p.Word, p => p);
    }

    private static WordView ToWordView(PronunciationWord word, IReadOnlyDictionary<string, PronunciationProgress> progress)
    {
        var state = progress.TryGetValue(word.Word, out var p) ? p.State : PronunciationState.New;
        var streak = progress.TryGetValue(word.Word, out var p2) ? p2.Streak : 0;
        return new WordView(word.Word, word.Ipa, $"/pronunciation-audio/{word.AudioUsFile}", $"/pronunciation-audio/{word.AudioUkFile}", state, streak);
    }
}
