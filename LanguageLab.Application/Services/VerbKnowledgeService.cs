using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>One row of a stage's table: the verb as the learner reads it, plus where they stand on it.</summary>
public sealed record VerbRowView(
    string V1, string V2, string V3, string Translation, int Group,
    double Mastery, int Streak, bool Passed, int Answers);

public sealed record StageView(int Group, string Title, int Total, int Passed, double Mastery);

public sealed record ProgressView(
    int LearnedPercent, IReadOnlyList<StageView> Stages, IReadOnlyList<VerbRowView> Verbs);

public sealed record AnswerResultView(string Verb, double Mastery, int Streak, bool Passed);

/// <summary>
/// The learner's standing on the whole catalog, and the one write the trainer makes: a
/// judged card updates its verb's row and appends to the answer log. The rules themselves
/// live in <see cref="VerbScoring"/> — this class only loads rows, applies them and saves.
/// </summary>
public class VerbKnowledgeService
{
    private readonly ApplicationDbContext _dbContext;

    public VerbKnowledgeService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProgressView> GetAsync(long userId)
    {
        var rows = await RowsAsync(userId);

        var verbs = IrregularVerbCatalog.Verbs.Select(v => Row(v, rows)).ToList();

        var stages = Enumerable.Range(1, IrregularVerbCatalog.GroupCount)
            .Select(group =>
            {
                var ofStage = verbs.Where(v => v.Group == group).ToList();

                return new StageView(
                    group,
                    IrregularVerbCatalog.GroupTitle(group),
                    ofStage.Count,
                    ofStage.Count(v => v.Passed),
                    ofStage.Average(v => v.Mastery));
            })
            .ToList();

        var passed = verbs.Count(v => v.Passed);

        return new ProgressView(
            (int)Math.Round(100.0 * passed / IrregularVerbCatalog.Verbs.Count),
            stages,
            verbs);
    }

    /// <summary>Null when the verb is not in the catalog — nothing is written in that case.</summary>
    public async Task<AnswerResultView?> ApplyAsync(
        long userId, string verb, PromptForm form, bool known, int responseMs,
        DrillMode mode, int? group, DateTime nowUtc)
    {
        if (IrregularVerbCatalog.Find(verb) == null)
        {
            return null;
        }

        var row = await _dbContext.VerbKnowledges
            .FirstOrDefaultAsync(k => k.UserId == userId && k.Verb == verb);

        if (row == null)
        {
            row = new VerbKnowledge { UserId = userId, Verb = verb };
            _dbContext.VerbKnowledges.Add(row);
        }

        var clamped = Math.Clamp(responseMs, 0, VerbScoring.MaxResponseMs);

        _dbContext.VerbAnswers.Add(new VerbAnswer
        {
            UserId = userId,
            Verb = verb,
            PromptForm = form,
            Known = known,
            ResponseMs = clamped,
            Mode = mode,
            Group = group,
            CreatedAt = nowUtc,
        });

        // The standing is a cache of the log, not a counter: replay every answer for this
        // verb rather than incrementing whatever the row happened to hold. Incrementing left
        // the counters permanently behind the log when another device answered in between; a
        // replay lands on what was actually answered however the writes interleaved. The
        // answer added just above is still unsaved, so it joins the fold here, last — its
        // timestamp is this call's, so it is the newest by construction.
        var logged = await _dbContext.VerbAnswers
            .Where(a => a.UserId == userId && a.Verb == verb)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => new { a.Known, a.ResponseMs })
            .ToListAsync();

        var replayed = logged
            .Select(a => (a.Known, a.ResponseMs))
            .Append((known, clamped));

        var tally = VerbScoring.Replay(replayed);

        row.Mastery = tally.Mastery;
        row.Streak = tally.Streak;
        row.Answers = tally.Answers;
        row.Knows = tally.Knows;
        row.LastAnsweredAt = nowUtc;

        await _dbContext.SaveChangesAsync();

        return new AnswerResultView(verb, row.Mastery, row.Streak, VerbScoring.Passed(row.Streak));
    }

    /// <summary>The selection rules' input, in the order the verbs were asked for.</summary>
    public async Task<IReadOnlyList<VerbStanding>> StandingsAsync(long userId, IReadOnlyList<IrregularVerb> verbs)
    {
        var rows = await RowsAsync(userId);

        return verbs.Select(v => Standing(v.V1, rows)).ToList();
    }

    private async Task<Dictionary<string, VerbKnowledge>> RowsAsync(long userId) =>
        await _dbContext.VerbKnowledges
            .Where(k => k.UserId == userId)
            .ToDictionaryAsync(k => k.Verb, StringComparer.Ordinal);

    private static VerbStanding Standing(string verb, IReadOnlyDictionary<string, VerbKnowledge> rows)
    {
        rows.TryGetValue(verb, out var row);

        return new VerbStanding(verb, row?.Mastery ?? 0, row?.Streak ?? 0, row?.Answers ?? 0);
    }

    private static VerbRowView Row(IrregularVerb verb, IReadOnlyDictionary<string, VerbKnowledge> rows)
    {
        rows.TryGetValue(verb.V1, out var row);

        return new VerbRowView(
            verb.V1,
            string.Join(" / ", verb.V2),
            string.Join(" / ", verb.V3),
            verb.Translation,
            verb.Group,
            row?.Mastery ?? 0,
            row?.Streak ?? 0,
            VerbScoring.Passed(row?.Streak ?? 0),
            row?.Answers ?? 0);
    }
}
