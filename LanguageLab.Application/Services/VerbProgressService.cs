using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record VerbRowView(string V1, string V2, string V3, string Translation, VerbState State, bool Flagged);

public sealed record FamilyView(
    string Key, string Title, FamilyStatus Status, int Total, int Learned, IReadOnlyList<VerbRowView> Verbs);

public sealed record GroupView(int Group, string Title, int Total, int Learned, IReadOnlyList<FamilyView> Families);

public sealed record ActiveSessionView(long Id, SessionMode Mode, int? Group, string? Family, int Answered, int Total);

public sealed record ProgressView(
    int LearnedPercent,
    IReadOnlyList<GroupView> Groups,
    bool MixedAvailable,
    bool ErrorsAvailable,
    ActiveSessionView? ActiveSession);

public sealed record ForgotResult(string V1, VerbState State);

/// <summary>
/// The learner's map: every group and family with its status, the verbs' states, what
/// session buttons are live, and the open session if there is one. Also owns the
/// Forgot button and the errors-only candidate list, which both read the same rows.
/// </summary>
public class VerbProgressService
{
    public const int RecentSessionsForErrors = 3;

    private readonly ApplicationDbContext _dbContext;

    public VerbProgressService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProgressView> GetAsync(long userId)
    {
        var rows = await RowsAsync(userId);
        var states = rows.ToDictionary(p => p.Key, p => p.Value.State, StringComparer.Ordinal);
        var path = LearningPath.Evaluate(states);
        var byFamily = path.ToDictionary(f => f.Family.Key, StringComparer.Ordinal);

        var groups = Enumerable.Range(1, IrregularVerbCatalog.GroupTitles.Count)
            .Select(group =>
            {
                var families = IrregularVerbCatalog.FamiliesOfGroup(group)
                    .Select(f => byFamily[f.Key])
                    .Select(f => new FamilyView(
                        f.Family.Key,
                        f.Family.Title,
                        f.Status,
                        f.Total,
                        f.Learned,
                        IrregularVerbCatalog.VerbsOf(f.Family.Key).Select(v => Row(v, rows)).ToList()))
                    .ToList();

                return new GroupView(
                    group,
                    IrregularVerbCatalog.GroupTitle(group),
                    families.Sum(f => f.Total),
                    families.Sum(f => f.Learned),
                    families);
            })
            .ToList();

        var learned = path.Sum(f => f.Learned);
        var percent = (int)Math.Round(100.0 * learned / IrregularVerbCatalog.Verbs.Count);
        var mistakes = await MistakeCandidatesAsync(userId);

        return new ProgressView(percent, groups, LearningPath.MixedAvailable(path), mistakes.Count > 0, await ActiveSessionAsync(userId));
    }

    public async Task<ForgotResult?> ForgotAsync(long userId, string v1, DateTime nowUtc)
    {
        if (IrregularVerbCatalog.Find(v1) == null)
        {
            return null;
        }

        var row = await GetOrCreateAsync(userId, v1, nowUtc);
        VerbStateMachine.ApplyForgot(row, nowUtc);
        await _dbContext.SaveChangesAsync();

        return new ForgotResult(v1, row.State);
    }

    /// <summary>Flagged verbs plus every verb answered wrongly in the last three finished sessions.</summary>
    public async Task<IReadOnlyList<string>> MistakeCandidatesAsync(long userId)
    {
        var flagged = await _dbContext.VerbProgresses
            .Where(p => p.UserId == userId && p.State == VerbState.Forgotten)
            .Select(p => p.Verb)
            .ToListAsync();

        var recentSessions = await _dbContext.VerbSessions
            .Where(s => s.UserId == userId && s.FinishedAt != null)
            .OrderByDescending(s => s.FinishedAt)
            .Take(RecentSessionsForErrors)
            .Select(s => s.Id)
            .ToListAsync();

        var wrong = await _dbContext.VerbAttempts
            .Where(a => a.UserId == userId && recentSessions.Contains(a.SessionId) && a.Outcome == AttemptOutcome.Wrong)
            .Select(a => a.Verb)
            .Distinct()
            .ToListAsync();

        return flagged.Concat(wrong).Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<Dictionary<string, VerbProgress>> RowsAsync(long userId) =>
        await _dbContext.VerbProgresses
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Verb, StringComparer.Ordinal);

    public async Task<VerbProgress> GetOrCreateAsync(long userId, string verb, DateTime nowUtc)
    {
        var row = await _dbContext.VerbProgresses.FirstOrDefaultAsync(p => p.UserId == userId && p.Verb == verb);

        if (row == null)
        {
            row = new VerbProgress { UserId = userId, Verb = verb, LastSeenAt = nowUtc };
            _dbContext.VerbProgresses.Add(row);
        }

        return row;
    }

    private async Task<ActiveSessionView?> ActiveSessionAsync(long userId)
    {
        var open = await _dbContext.VerbSessions
            .Where(s => s.UserId == userId && s.FinishedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        if (open == null)
        {
            return null;
        }

        var total = await _dbContext.VerbTasks.CountAsync(t => t.SessionId == open.Id);
        var answered = await _dbContext.VerbTasks.CountAsync(t => t.SessionId == open.Id && t.Outcome != null);

        return new ActiveSessionView(open.Id, open.Mode, open.Group, open.Family, answered, total);
    }

    private static VerbRowView Row(IrregularVerb verb, IReadOnlyDictionary<string, VerbProgress> rows)
    {
        rows.TryGetValue(verb.V1, out var p);

        return new VerbRowView(
            verb.V1,
            string.Join(" / ", verb.V2),
            string.Join(" / ", verb.V3),
            verb.Translation,
            p?.State ?? VerbState.New,
            p?.ManuallyFlaggedAt != null);
    }
}
