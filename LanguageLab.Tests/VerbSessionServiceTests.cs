using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class VerbSessionServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (VerbProgressService Progress, VerbSessionService Sessions) Services(ApplicationDbContext db, int seed = 1)
    {
        var progress = new VerbProgressService(db);
        return (progress, new VerbSessionService(db, progress, new Random(seed)));
    }

    [Fact]
    public async Task Start_learn_creates_a_full_queue_with_cards_first()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);

        var result = await sessions.StartAsync(1, SessionMode.Learn, "same", null, Now);

        Assert.NotNull(result.Session);
        Assert.Null(result.Refusal);
        Assert.Equal(16, result.Session.Tasks.Count);
        Assert.Equal(ExerciseType.Card, result.Session.Tasks.OrderBy(t => t.Order).First().Type);
    }

    [Fact]
    public async Task Start_opens_a_family_far_down_the_path_to_a_user_with_no_progress()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);

        var result = await sessions.StartAsync(1, SessionMode.Learn, "ought", null, Now);

        Assert.NotNull(result.Session);
        Assert.Null(result.Refusal);
        Assert.Equal("ought", result.Session.Family);
    }

    [Fact]
    public async Task Start_refuses_an_unknown_family()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);

        var result = await sessions.StartAsync(1, SessionMode.Learn, "nope", null, Now);

        Assert.Null(result.Session);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public async Task Starting_a_new_session_closes_a_previous_open_one()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);

        var first = await sessions.StartAsync(1, SessionMode.Learn, "same", null, Now);
        var second = await sessions.StartAsync(1, SessionMode.Learn, "same", null, Now.AddMinutes(1));

        Assert.NotNull(second.Session);
        var closed = await db.VerbSessions.SingleAsync(s => s.Id == first.Session!.Id);
        Assert.NotNull(closed.FinishedAt);
    }

    [Fact]
    public async Task Next_returns_the_first_pending_task_and_counts()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var started = (await sessions.StartAsync(1, SessionMode.Learn, "back", null, Now)).Session!;

        var next = await sessions.NextAsync(started.Id);

        Assert.NotNull(next.Task);
        Assert.Equal(0, next.Answered);
        Assert.Equal(9, next.Total);
    }

    [Fact]
    public async Task Answering_a_card_advances_the_verb_to_learning_1()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var started = (await sessions.StartAsync(1, SessionMode.Learn, "back", null, Now)).Session!;
        var task = (await sessions.NextAsync(started.Id)).Task!;

        var answer = await sessions.AnswerAsync(started.Id, task.Id, "seen", null, Now);

        Assert.NotNull(answer);
        Assert.Equal(AttemptOutcome.Correct, answer.Outcome);
        Assert.True(answer.TaskComplete);

        var progress = await db.VerbProgresses.SingleAsync(p => p.Verb == task.Verb);
        Assert.Equal(VerbState.Learning1, progress.State);
    }

    [Fact]
    public async Task A_second_answer_to_the_same_task_is_null()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var started = (await sessions.StartAsync(1, SessionMode.Learn, "back", null, Now)).Session!;
        var task = (await sessions.NextAsync(started.Id)).Task!;

        await sessions.AnswerAsync(started.Id, task.Id, "seen", null, Now);
        var second = await sessions.AnswerAsync(started.Id, task.Id, "seen", null, Now);

        Assert.Null(second);
    }

    [Fact]
    public async Task A_wrong_gap_type_answer_queues_two_returns_and_reports_will_return()
    {
        await using var db = NewContext();
        var (progress, sessions) = Services(db);

        // Get "go" to Learning3 (level-3, GapType) directly via progress rows.
        var row = new VerbProgress { UserId = 1, Verb = "go", State = VerbState.Learning3, LastSeenAt = Now };
        db.VerbProgresses.Add(row);
        await db.SaveChangesAsync();

        var started = (await sessions.StartAsync(1, SessionMode.Learn, "core", null, Now)).Session!;
        var beforeCount = started.Tasks.Count;

        var goTask = started.Tasks.First(t => t.Verb == "go" && t.Type == ExerciseType.GapType);
        var answer = await sessions.AnswerAsync(started.Id, goTask.Id, "goed", null, Now);

        Assert.NotNull(answer);
        Assert.Equal(AttemptOutcome.Wrong, answer.Outcome);
        Assert.True(answer.WillReturn);

        var allTasks = await db.VerbTasks.Where(t => t.SessionId == started.Id).ToListAsync();
        Assert.Equal(beforeCount + 2, allTasks.Count);
        Assert.Equal(2, allTasks.Count(t => t.IsReturn));

        var orders = allTasks.OrderBy(t => t.Order).Select(t => t.Order).ToList();
        Assert.Equal(Enumerable.Range(0, allTasks.Count), orders);
    }

    [Fact]
    public async Task A_return_task_does_not_insert_further_returns()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var row = new VerbProgress { UserId = 1, Verb = "go", State = VerbState.Learning3, LastSeenAt = Now };
        db.VerbProgresses.Add(row);
        await db.SaveChangesAsync();

        var started = (await sessions.StartAsync(1, SessionMode.Learn, "core", null, Now)).Session!;
        var goTask = started.Tasks.First(t => t.Verb == "go" && t.Type == ExerciseType.GapType);
        await sessions.AnswerAsync(started.Id, goTask.Id, "goed", null, Now);

        var countAfterFirstMiss = await db.VerbTasks.CountAsync(t => t.SessionId == started.Id);

        var returnTask = await db.VerbTasks.FirstAsync(t => t.SessionId == started.Id && t.IsReturn && t.Outcome == null);
        var answer = await sessions.AnswerAsync(started.Id, returnTask.Id, "goed", null, Now);

        Assert.False(answer!.WillReturn);
        var countAfterSecondMiss = await db.VerbTasks.CountAsync(t => t.SessionId == started.Id);
        Assert.Equal(countAfterFirstMiss, countAfterSecondMiss);
    }

    [Fact]
    public async Task A_near_miss_spelling_stays_pending_once_then_counts_as_a_mistake()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var row = new VerbProgress { UserId = 1, Verb = "buy", State = VerbState.Learning3, LastSeenAt = Now };
        db.VerbProgresses.Add(row);
        await db.SaveChangesAsync();

        var started = (await sessions.StartAsync(1, SessionMode.Learn, "ought", null, Now)).Session!;
        var task = started.Tasks.First(t => t.Verb == "buy" && t.Type == ExerciseType.GapType && t.FormAsked == FormAsked.V2);

        var first = await sessions.AnswerAsync(started.Id, task.Id, "boght", null, Now);
        Assert.Equal(AttemptOutcome.Neutral, first!.Outcome);
        Assert.False(first.TaskComplete);

        var stillPending = await db.VerbTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Null(stillPending.Outcome);
        Assert.Equal(1, stillPending.NeutralCount);

        var second = await sessions.AnswerAsync(started.Id, task.Id, "boght", null, Now);
        Assert.Equal(AttemptOutcome.Wrong, second!.Outcome);
        Assert.True(second.TaskComplete);
    }

    [Fact]
    public async Task A_match_pair_flow_completes_wrong_if_any_pair_was_wrong()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var started = (await sessions.StartAsync(1, SessionMode.Learn, "ought", null, Now)).Session!;
        var matchTask = started.Tasks.First(t => t.Type == ExerciseType.Match);
        var payload = matchTask.GetPayload();

        foreach (var pair in payload.Pairs!.SkipLast(1))
        {
            var answer = await sessions.AnswerAsync(started.Id, matchTask.Id, $"{pair.Left}={pair.Right}", null, Now);
            Assert.False(answer!.TaskComplete);
            Assert.Equal(AttemptOutcome.Correct, answer.Outcome);
        }

        var last = payload.Pairs.Last();
        var wrongAnswer = await sessions.AnswerAsync(started.Id, matchTask.Id, $"{last.Left}=wrongform", null, Now);

        Assert.True(wrongAnswer!.TaskComplete);
        Assert.Equal(AttemptOutcome.Wrong, wrongAnswer.Outcome);

        var completedTask = await db.VerbTasks.SingleAsync(t => t.Id == matchTask.Id);
        Assert.Equal(AttemptOutcome.Wrong, completedTask.Outcome);
    }

    [Fact]
    public async Task Triple_type_with_one_wrong_field_logs_two_attempts_and_is_wrong()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var row = new VerbProgress { UserId = 1, Verb = "buy", State = VerbState.Learning3, LastSeenAt = Now };
        db.VerbProgresses.Add(row);
        await db.SaveChangesAsync();

        var started = (await sessions.StartAsync(1, SessionMode.Learn, "ought", null, Now)).Session!;
        var task = started.Tasks.First(t => t.Verb == "buy" && t.Type == ExerciseType.TripleType);

        var answer = await sessions.AnswerAsync(started.Id, task.Id, "bought|zzzzzzz", null, Now);

        Assert.Equal(AttemptOutcome.Wrong, answer!.Outcome);
        Assert.True(answer.TaskComplete);

        var attempts = await db.VerbAttempts.Where(a => a.TaskId == task.Id).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(1, attempts.Count(a => a.Outcome == AttemptOutcome.Wrong));
    }

    [Fact]
    public async Task Finish_sets_totals_and_marks_a_verb_learned_after_two_clean_sessions()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);

        var row = new VerbProgress { UserId = 1, Verb = "go", State = VerbState.Learning3, CleanSessions = 1, LastSeenAt = Now };
        db.VerbProgresses.Add(row);
        await db.SaveChangesAsync();

        var started = (await sessions.StartAsync(1, SessionMode.Learn, "core", null, Now)).Session!;

        // Answer every task; "go"'s level-3 tasks get their correct forms, everything else
        // is answered however it comes so the queue empties — only "go"'s cleanliness matters here.
        var view = await sessions.NextAsync(started.Id);

        while (view.Task != null)
        {
            var task = view.Task;
            string answer;

            if (task.Verb == "go" && task.Type == ExerciseType.GapType)
            {
                answer = task.FormAsked == FormAsked.V2 ? "went" : "gone";
            }
            else if (task.Verb == "go" && task.Type == ExerciseType.TripleType)
            {
                answer = "went|gone";
            }
            else if (task.Type == ExerciseType.FormPick)
            {
                answer = task.GetPayload().Correct!;
            }
            else if (task.Type == ExerciseType.Card)
            {
                answer = "seen";
            }
            else if (task.Type == ExerciseType.GapChoice)
            {
                answer = task.GetPayload().Options!.First();
            }
            else if (task.Type == ExerciseType.Match)
            {
                var payload = task.GetPayload();
                var pair = payload.Pairs!.First(p => !(payload.Matched ?? []).Any(m => m.Left == p.Left));
                answer = $"{pair.Left}={pair.Right}";
            }
            else
            {
                answer = "x";
            }

            await sessions.AnswerAsync(started.Id, task.Id, answer, null, Now);
            view = await sessions.NextAsync(started.Id);
        }

        var summary = await sessions.FinishAsync(started.Id, Now);

        Assert.Contains("go", summary.Learned);

        var updated = await db.VerbProgresses.SingleAsync(p => p.Verb == "go");
        Assert.Equal(VerbState.Learned, updated.State);
    }

    [Fact]
    public async Task Finish_is_idempotent()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var started = (await sessions.StartAsync(1, SessionMode.Learn, "back", null, Now)).Session!;

        var first = await sessions.FinishAsync(started.Id, Now);
        var second = await sessions.FinishAsync(started.Id, Now.AddMinutes(1));

        Assert.Equal(first.Total, second.Total);
        Assert.Equal(first.Correct, second.Correct);
    }

    [Fact]
    public async Task Errors_only_from_a_session_picks_only_the_wrong_verbs()
    {
        await using var db = NewContext();
        var (_, sessions) = Services(db);
        var learnSession = (await sessions.StartAsync(1, SessionMode.Learn, "same", null, Now)).Session!;

        var cutCard = learnSession.Tasks.First(t => t.Verb == "cut" && t.Type == ExerciseType.Card);
        await sessions.AnswerAsync(learnSession.Id, cutCard.Id, "seen", null, Now);

        var cutTask = (await db.VerbTasks.Where(t => t.SessionId == learnSession.Id && t.Verb == "cut" && t.Outcome == null)
            .OrderBy(t => t.Order).FirstOrDefaultAsync());

        if (cutTask != null)
        {
            await sessions.AnswerAsync(learnSession.Id, cutTask.Id, "totallywrong", null, Now);
        }

        await sessions.FinishAsync(learnSession.Id, Now);

        var errorsResult = await sessions.StartAsync(1, SessionMode.ErrorsOnly, null, learnSession.Id, Now.AddMinutes(5));

        Assert.NotNull(errorsResult.Session);
        Assert.All(errorsResult.Session.Tasks, t => Assert.Equal("cut", t.Verb));
    }

}
