using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>Session when it started; Refusal (a sentence for the learner) when it could not.</summary>
public sealed record StartResult(VerbSession? Session, string? Refusal);

public sealed record NextView(VerbTask? Task, int Answered, int Total);

/// <summary>
/// Feedback for one answer. TaskComplete is false after a neutral spelling and after a
/// Match pair while pairs remain; WillReturn says the verb comes back later in this session.
/// </summary>
public sealed record AnswerView(
    AttemptOutcome Outcome,
    bool TaskComplete,
    string CorrectAnswer,
    string Triplet,
    string Explanation,
    ErrorKind? ErrorKind,
    bool WillReturn,
    IReadOnlyList<MatchPair>? Matched);

public sealed record MistakeView(string V1, string V2, string V3, string Translation, int WrongCount);

public sealed record SummaryView(int Total, int Correct, IReadOnlyList<MistakeView> Mistakes, IReadOnlyList<string> Learned);

/// <summary>
/// The life of one session: planning and storing the queue, serving the next task,
/// checking answers (all rules live in the Domain), logging attempts, updating each
/// verb's standing, inserting returns after a mistake, and closing with a summary.
/// The queue is in the database, so a reload picks up where the learner was.
/// </summary>
public class VerbSessionService
{
    public const int FirstReturnGap = 2;
    public const int SecondReturnGap = 5;

    private readonly ApplicationDbContext _dbContext;
    private readonly VerbProgressService _progress;
    private readonly Random _rng;

    public VerbSessionService(ApplicationDbContext dbContext, VerbProgressService progress)
        : this(dbContext, progress, Random.Shared)
    {
    }

    public VerbSessionService(ApplicationDbContext dbContext, VerbProgressService progress, Random rng)
    {
        _dbContext = dbContext;
        _progress = progress;
        _rng = rng;
    }

    public async Task<StartResult> StartAsync(long userId, SessionMode mode, string? family, long? fromSessionId, DateTime nowUtc)
    {
        var rows = await _progress.RowsAsync(userId);
        int? group = null;
        IReadOnlyList<string> mistakes = [];

        switch (mode)
        {
            case SessionMode.Learn:
                var known = family == null ? null : IrregularVerbCatalog.FindFamily(family);

                if (known == null)
                {
                    return new StartResult(null, "That family does not exist.");
                }

                var path = LearningPath.Evaluate(rows.ToDictionary(p => p.Key, p => p.Value.State, StringComparer.Ordinal));
                var index = IrregularVerbCatalog.FamilyIndex(known.Key);

                if (path[index].Status == FamilyStatus.Locked)
                {
                    var previous = path[index - 1].Family;
                    return new StartResult(null, $"Finish \"{previous.Title}\" before this one — {LearningPath.DoneShare:P0} of its verbs must be learned.");
                }

                group = known.Group;
                break;

            case SessionMode.ErrorsOnly:
                mistakes = fromSessionId is { } sourceId
                    ? await WrongVerbsAsync(userId, sourceId)
                    : await _progress.MistakeCandidatesAsync(userId);
                break;
        }

        var specs = SessionPlanner.Plan(new PlanRequest(mode, family, mistakes), rows, _rng);

        if (specs.Count == 0)
        {
            return new StartResult(null, "Nothing to train here yet.");
        }

        await CloseOpenSessionsAsync(userId, nowUtc);

        var session = new VerbSession
        {
            UserId = userId,
            Mode = mode,
            Group = group,
            Family = mode == SessionMode.Learn ? family : null,
            StartedAt = nowUtc,
        };

        for (var i = 0; i < specs.Count; i++)
        {
            session.Tasks.Add(ToTask(specs[i], i, isReturn: false));
        }

        _dbContext.VerbSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        return new StartResult(session, null);
    }

    public Task<VerbSession?> FindAsync(long sessionId, long userId) =>
        _dbContext.VerbSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

    public async Task<NextView> NextAsync(long sessionId)
    {
        var total = await _dbContext.VerbTasks.CountAsync(t => t.SessionId == sessionId);
        var answered = await _dbContext.VerbTasks.CountAsync(t => t.SessionId == sessionId && t.Outcome != null);

        var task = await _dbContext.VerbTasks
            .Where(t => t.SessionId == sessionId && t.Outcome == null)
            .OrderBy(t => t.Order)
            .FirstOrDefaultAsync();

        return new NextView(task, answered, total);
    }

    /// <summary>Null when the task is already answered — a double click, not an error.</summary>
    public async Task<AnswerView?> AnswerAsync(long sessionId, long taskId, string answer, int? responseMs, DateTime nowUtc)
    {
        var task = await _dbContext.VerbTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.SessionId == sessionId);

        if (task == null || task.Outcome != null)
        {
            return null;
        }

        var session = await _dbContext.VerbSessions.FirstAsync(s => s.Id == sessionId);
        var verb = IrregularVerbCatalog.Find(task.Verb)!;
        var payload = task.GetPayload();

        var view = task.Type switch
        {
            ExerciseType.Card => await CardAsync(session, task, verb, nowUtc),
            ExerciseType.Match => await MatchAsync(session, task, verb, payload, answer, responseMs, nowUtc),
            ExerciseType.TripleType => await TripleAsync(session, task, verb, payload, answer, responseMs, nowUtc),
            _ => await SingleAsync(session, task, verb, payload, answer, responseMs, nowUtc),
        };

        await _dbContext.SaveChangesAsync();
        return view;
    }

    public async Task<SummaryView> FinishAsync(long sessionId, DateTime nowUtc)
    {
        var session = await _dbContext.VerbSessions.Include(s => s.Tasks).FirstAsync(s => s.Id == sessionId);
        var answered = session.Tasks.Where(t => t.Outcome != null).ToList();
        var learned = new List<string>();

        if (session.FinishedAt == null)
        {
            session.FinishedAt = nowUtc;
            session.Total = answered.Count;
            session.Correct = answered.Count(t => t.Outcome == AttemptOutcome.Correct);

            // A clean session is judged on production tasks only: that is where "learned" is earned.
            foreach (var group in answered.Where(t => t.Level == TaskLevels.Production).GroupBy(t => t.Verb))
            {
                var row = await _progress.GetOrCreateAsync(session.UserId, group.Key, nowUtc);
                var before = row.State;
                VerbStateMachine.ApplySessionEnd(row, clean: group.All(t => t.Outcome == AttemptOutcome.Correct), nowUtc);

                if (row.State == VerbState.Learned && before != VerbState.Learned)
                {
                    learned.Add(group.Key);
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        var wrong = await _dbContext.VerbAttempts
            .Where(a => a.SessionId == sessionId && a.Outcome == AttemptOutcome.Wrong)
            .GroupBy(a => a.Verb)
            .Select(g => new { Verb = g.Key, Count = g.Count() })
            .ToListAsync();

        var mistakes = IrregularVerbCatalog.Verbs
            .Where(v => wrong.Any(w => w.Verb == v.V1))
            .Select(v => new MistakeView(
                v.V1, string.Join(" / ", v.V2), string.Join(" / ", v.V3), v.Translation, wrong.First(w => w.Verb == v.V1).Count))
            .ToList();

        return new SummaryView(session.Total, session.Correct, mistakes, learned);
    }

    private async Task<AnswerView> CardAsync(VerbSession session, VerbTask task, IrregularVerb verb, DateTime nowUtc)
    {
        Complete(task, AttemptOutcome.Correct, "seen", null, nowUtc);
        Log(session, task, verb.V1, FormAsked.Recognition, "seen", AttemptOutcome.Correct, null, null, nowUtc);

        var row = await _progress.GetOrCreateAsync(session.UserId, verb.V1, nowUtc);
        VerbStateMachine.ApplyCard(row, nowUtc);

        return new AnswerView(AttemptOutcome.Correct, true, verb.Triplet, verb.Triplet, Explanations.For(verb, FormAsked.Both, null, null), null, false, null);
    }

    private async Task<AnswerView> SingleAsync(
        VerbSession session, VerbTask task, IrregularVerb verb, TaskPayload payload, string answer, int? responseMs, DateTime nowUtc)
    {
        var (result, correctAnswer, explanation) = task.Type switch
        {
            ExerciseType.OddOne => CheckOddOne(payload, answer),
            ExerciseType.FormPick => CheckFormPick(verb, payload, answer),
            ExerciseType.GapChoice => CheckForm(verb, task, payload, answer, typed: false),
            _ => CheckForm(verb, task, payload, answer, typed: true),
        };

        Log(session, task, verb.V1, task.FormAsked, result.Normalized, result.Outcome, result.Kind, responseMs, nowUtc);

        if (result.Outcome == AttemptOutcome.Neutral)
        {
            task.NeutralCount++;
            return new AnswerView(AttemptOutcome.Neutral, false, correctAnswer, verb.Triplet, explanation, result.Kind, false, null);
        }

        Complete(task, result.Outcome, result.Normalized, result.Kind, nowUtc);
        var willReturn = await ApplyOutcomeAsync(session, task, verb, result.Outcome, task.FormAsked, wrongFields: 1, nowUtc);

        return new AnswerView(result.Outcome, true, correctAnswer, verb.Triplet, explanation, result.Kind, willReturn, null);
    }

    private async Task<AnswerView> TripleAsync(
        VerbSession session, VerbTask task, IrregularVerb verb, TaskPayload payload, string answer, int? responseMs, DateTime nowUtc)
    {
        var parts = answer.Split('|', 2);
        var v2 = AnswerChecker.Check(verb, FormAsked.V2, parts[0], typed: true, task.NeutralCount);
        var v3 = AnswerChecker.Check(verb, FormAsked.V3, parts.Length > 1 ? parts[1] : "", typed: true, task.NeutralCount);
        var correctAnswer = $"{string.Join(" / ", verb.V2)} | {string.Join(" / ", verb.V3)}";
        var normalized = $"{v2.Normalized}|{v3.Normalized}";

        var results = new[] { (FormAsked.V2, v2), (FormAsked.V3, v3) };
        var anyWrong = results.Any(r => r.Item2.Outcome == AttemptOutcome.Wrong);
        var anyNeutral = results.Any(r => r.Item2.Outcome == AttemptOutcome.Neutral);

        if (!anyWrong && anyNeutral)
        {
            // Every field is right or a near miss: one more try before it counts.
            Log(session, task, verb.V1, FormAsked.Both, normalized, AttemptOutcome.Neutral, ErrorKind.Spelling, responseMs, nowUtc);
            task.NeutralCount++;
            return new AnswerView(AttemptOutcome.Neutral, false, correctAnswer, verb.Triplet, Explanations.For(verb, FormAsked.Both, null, null), ErrorKind.Spelling, false, null);
        }

        foreach (var (form, result) in results)
        {
            Log(session, task, verb.V1, form, result.Normalized, result.Outcome, result.Kind, responseMs, nowUtc);
        }

        var outcome = anyWrong ? AttemptOutcome.Wrong : AttemptOutcome.Correct;
        var kind = results.Select(r => r.Item2).FirstOrDefault(r => r.Outcome == AttemptOutcome.Wrong)?.Kind;
        var wrongFields = results.Count(r => r.Item2.Outcome == AttemptOutcome.Wrong);
        var wrongForm = wrongFields == 2 ? FormAsked.Both : v2.Outcome == AttemptOutcome.Wrong ? FormAsked.V2 : FormAsked.V3;

        Complete(task, outcome, normalized, kind, nowUtc);
        var willReturn = await ApplyOutcomeAsync(session, task, verb, outcome, wrongForm, wrongFields, nowUtc);

        return new AnswerView(outcome, true, correctAnswer, verb.Triplet, Explanations.For(verb, wrongForm, null, kind), kind, willReturn, null);
    }

    private async Task<AnswerView> MatchAsync(
        VerbSession session, VerbTask task, IrregularVerb anchor, TaskPayload payload, string answer, int? responseMs, DateTime nowUtc)
    {
        var parts = answer.Split('=', 2);
        var left = parts[0].Trim();
        var right = parts.Length > 1 ? parts[1].Trim() : "";
        var pair = payload.Pairs!.FirstOrDefault(p => p.Left == left);
        var form = payload.Form ?? FormAsked.V2;

        payload.AttemptedLefts ??= [];

        if (pair == null || payload.AttemptedLefts.Contains(left))
        {
            return new AnswerView(AttemptOutcome.Wrong, false, "", anchor.Triplet, "Pick a verb on the left that is not resolved yet.", ErrorKind.Other, false, payload.Matched);
        }

        var verb = IrregularVerbCatalog.Find(left)!;
        var correct = pair.Right == right;
        var outcome = correct ? AttemptOutcome.Correct : AttemptOutcome.Wrong;
        var kind = correct ? null : (ErrorKind?)ErrorKind.WrongFamily;

        Log(session, task, verb.V1, form, right, outcome, kind, responseMs, nowUtc);

        var row = await _progress.GetOrCreateAsync(session.UserId, verb.V1, nowUtc);
        var willReturn = false;

        payload.AttemptedLefts.Add(left);

        if (correct)
        {
            payload.Matched!.Add(pair);
            VerbStateMachine.ApplyCorrect(row, nowUtc);
        }
        else
        {
            payload.WrongPairs = (payload.WrongPairs ?? 0) + 1;
            VerbStateMachine.ApplyWrong(row, form, 1, nowUtc);

            if (!task.IsReturn)
            {
                await InsertReturnsAsync(session, task, verb, row.State, nowUtc);
                willReturn = true;
            }
        }

        task.SetPayload(payload);

        if (payload.AttemptedLefts.Count == payload.Pairs!.Count)
        {
            Complete(task, payload.WrongPairs > 0 ? AttemptOutcome.Wrong : AttemptOutcome.Correct, "matched", null, nowUtc);
        }

        return new AnswerView(
            outcome, task.Outcome != null, pair.Right, verb.Triplet, Explanations.For(verb, form, null, kind), kind, willReturn, payload.Matched);
    }

    private static (CheckResult Result, string CorrectAnswer, string Explanation) CheckForm(
        IrregularVerb verb, VerbTask task, TaskPayload payload, string answer, bool typed)
    {
        var result = AnswerChecker.Check(verb, task.FormAsked, answer, typed, task.NeutralCount);
        var explanation = Explanations.For(verb, task.FormAsked, payload.Tense, result.Kind);

        return (result, string.Join(" / ", verb.Forms(task.FormAsked)), explanation);
    }

    private static (CheckResult Result, string CorrectAnswer, string Explanation) CheckOddOne(TaskPayload payload, string answer)
    {
        var normalized = AnswerChecker.Normalize(answer);
        var odd = IrregularVerbCatalog.Find(payload.Hint!)!;
        var correct = normalized == payload.Correct;
        var explanation = $"{payload.Correct} is from {odd.V1}, a verb that changes: {odd.Triplet}. The other three never do.";

        return (new CheckResult(correct ? AttemptOutcome.Correct : AttemptOutcome.Wrong, correct ? null : ErrorKind.WrongFamily, normalized), payload.Correct!, explanation);
    }

    private static (CheckResult Result, string CorrectAnswer, string Explanation) CheckFormPick(IrregularVerb verb, TaskPayload payload, string answer)
    {
        var normalized = AnswerChecker.Normalize(answer);
        var correct = normalized == payload.Correct;
        var kind = correct ? null : payload.Correct == "v3" ? (ErrorKind?)ErrorKind.V2ForV3 : ErrorKind.V3ForV2;
        var asked = payload.Correct == "v2" ? FormAsked.V2 : FormAsked.V3;
        var label = payload.Correct == "v2" ? "Past Simple (V2)" : "Present Perfect (V3)";

        return (new CheckResult(correct ? AttemptOutcome.Correct : AttemptOutcome.Wrong, kind, normalized), label, Explanations.For(verb, asked, payload.Tense, kind));
    }

    /// <summary>Updates the verb's standing and, after a first mistake, queues two returns. True when the verb comes back.</summary>
    private async Task<bool> ApplyOutcomeAsync(
        VerbSession session, VerbTask task, IrregularVerb verb, AttemptOutcome outcome, FormAsked wrongForm, int wrongFields, DateTime nowUtc)
    {
        var row = await _progress.GetOrCreateAsync(session.UserId, verb.V1, nowUtc);

        if (outcome == AttemptOutcome.Correct)
        {
            VerbStateMachine.ApplyCorrect(row, nowUtc);
            return false;
        }

        VerbStateMachine.ApplyWrong(row, wrongForm, wrongFields, nowUtc);

        if (task.IsReturn)
        {
            return false;
        }

        await InsertReturnsAsync(session, task, verb, row.State, nowUtc);
        return true;
    }

    private async Task InsertReturnsAsync(VerbSession session, VerbTask current, IrregularVerb verb, VerbState state, DateTime nowUtc)
    {
        var tasks = await _dbContext.VerbTasks.Where(t => t.SessionId == session.Id).OrderBy(t => t.Order).ToListAsync();
        var appearance = tasks.Count(t => t.Verb == verb.V1);
        var specs = SessionPlanner.Returns(verb, state, current.Level, appearance, _rng);
        var position = tasks.FindIndex(t => t.Id == current.Id);

        var first = ToTask(specs[0], 0, isReturn: true);
        var second = ToTask(specs[1], 0, isReturn: true);
        tasks.Insert(Math.Min(position + FirstReturnGap, tasks.Count), first);
        tasks.Insert(Math.Min(position + SecondReturnGap, tasks.Count), second);

        for (var i = 0; i < tasks.Count; i++)
        {
            tasks[i].Order = i;
        }

        first.SessionId = session.Id;
        second.SessionId = session.Id;
        _dbContext.VerbTasks.AddRange(first, second);
    }

    private async Task<IReadOnlyList<string>> WrongVerbsAsync(long userId, long sessionId) =>
        await _dbContext.VerbAttempts
            .Where(a => a.UserId == userId && a.SessionId == sessionId && a.Outcome == AttemptOutcome.Wrong)
            .Select(a => a.Verb)
            .Distinct()
            .ToListAsync();

    private async Task CloseOpenSessionsAsync(long userId, DateTime nowUtc)
    {
        var open = await _dbContext.VerbSessions.Include(s => s.Tasks).Where(s => s.UserId == userId && s.FinishedAt == null).ToListAsync();

        // Abandoned, not finished: the answered tasks count, the clean-session rule does not run.
        foreach (var session in open)
        {
            session.FinishedAt = nowUtc;
            session.Total = session.Tasks.Count(t => t.Outcome != null);
            session.Correct = session.Tasks.Count(t => t.Outcome == AttemptOutcome.Correct);
        }
    }

    private static VerbTask ToTask(TaskSpec spec, int order, bool isReturn)
    {
        var task = new VerbTask
        {
            Order = order,
            Type = spec.Type,
            Verb = spec.Verb.V1,
            FormAsked = spec.FormAsked,
            Level = spec.Level,
            IsReturn = isReturn,
        };

        task.SetPayload(spec.Payload);
        return task;
    }

    private static void Complete(VerbTask task, AttemptOutcome outcome, string answer, ErrorKind? kind, DateTime nowUtc)
    {
        task.Outcome = outcome;
        task.AnswerGiven = answer;
        task.ErrorKind = kind;
        task.AnsweredAt = nowUtc;
    }

    private void Log(
        VerbSession session, VerbTask task, string verb, FormAsked form, string answer,
        AttemptOutcome outcome, ErrorKind? kind, int? responseMs, DateTime nowUtc)
    {
        _dbContext.VerbAttempts.Add(new VerbAttempt
        {
            UserId = session.UserId,
            Verb = verb,
            Session = session,
            Task = task,
            Type = task.Type,
            FormAsked = form,
            AnswerGiven = answer,
            Outcome = outcome,
            ErrorKind = kind,
            ResponseMs = responseMs,
            CreatedAt = nowUtc,
        });
    }
}
