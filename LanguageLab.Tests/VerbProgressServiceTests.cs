using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class VerbProgressServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task An_empty_user_has_only_the_first_family_available_and_nothing_active()
    {
        await using var db = NewContext();
        var view = await new VerbProgressService(db).GetAsync(1);

        Assert.Equal(0, view.LearnedPercent);
        Assert.False(view.MixedAvailable);
        Assert.False(view.ErrorsAvailable);
        Assert.Null(view.ActiveSession);

        var group1 = view.Groups.Single(g => g.Group == 1);
        Assert.True(group1.Unlocked);
        Assert.Equal(FamilyStatus.Available, group1.Families.Single(f => f.Key == "same").Status);

        var group2 = view.Groups.Single(g => g.Group == 2);
        Assert.False(group2.Unlocked);
        Assert.All(group2.Families, f => Assert.Equal(FamilyStatus.Locked, f.Status));

        var cut = group1.Families.Single(f => f.Key == "same").Verbs.Single(v => v.V1 == "cut");
        Assert.Equal(VerbState.New, cut.State);
        Assert.False(cut.Flagged);
    }

    [Fact]
    public async Task Forgot_creates_a_row_and_flags_it()
    {
        await using var db = NewContext();
        var service = new VerbProgressService(db);

        var result = await service.ForgotAsync(1, "cut", Now);

        Assert.NotNull(result);
        Assert.Equal(VerbState.Forgotten, result.State);

        var row = await db.VerbProgresses.SingleAsync();
        Assert.Equal("cut", row.Verb);
        Assert.Equal(Now, row.ManuallyFlaggedAt);
    }

    [Fact]
    public async Task Forgot_updates_an_existing_row_instead_of_duplicating_it()
    {
        await using var db = NewContext();
        db.VerbProgresses.Add(new VerbProgress { UserId = 1, Verb = "cut", State = VerbState.Learning2, Streak = 2, LastSeenAt = Now });
        await db.SaveChangesAsync();

        await new VerbProgressService(db).ForgotAsync(1, "cut", Now);

        Assert.Equal(1, await db.VerbProgresses.CountAsync());
        var row = await db.VerbProgresses.SingleAsync();
        Assert.Equal(VerbState.Forgotten, row.State);
    }

    [Fact]
    public async Task Forgot_is_null_for_an_unknown_verb()
    {
        await using var db = NewContext();
        Assert.Null(await new VerbProgressService(db).ForgotAsync(1, "walk", Now));
        Assert.Equal(0, await db.VerbProgresses.CountAsync());
    }

    [Fact]
    public async Task Mistake_candidates_include_flagged_verbs_and_wrong_answers_from_recent_finished_sessions()
    {
        await using var db = NewContext();
        db.VerbProgresses.Add(new VerbProgress { UserId = 1, Verb = "put", State = VerbState.Forgotten, LastSeenAt = Now });

        var session = new VerbSession { UserId = 1, Mode = SessionMode.Learn, Family = "same", StartedAt = Now, FinishedAt = Now };
        db.VerbSessions.Add(session);
        await db.SaveChangesAsync();

        db.VerbAttempts.Add(new VerbAttempt
        {
            UserId = 1, Verb = "cut", SessionId = session.Id, TaskId = 0, Type = ExerciseType.GapType,
            FormAsked = FormAsked.V2, AnswerGiven = "cutted", Outcome = AttemptOutcome.Wrong, ErrorKind = ErrorKind.EdSuffix, CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var view = await new VerbProgressService(db).GetAsync(1);

        Assert.True(view.ErrorsAvailable);
    }

    [Fact]
    public async Task Mistake_candidates_ignore_attempts_from_sessions_older_than_the_last_three()
    {
        await using var db = NewContext();

        for (var i = 0; i < 4; i++)
        {
            var session = new VerbSession
            {
                UserId = 1, Mode = SessionMode.Learn, Family = "same", StartedAt = Now.AddMinutes(i), FinishedAt = Now.AddMinutes(i),
            };
            db.VerbSessions.Add(session);
            await db.SaveChangesAsync();

            db.VerbAttempts.Add(new VerbAttempt
            {
                UserId = 1, Verb = $"verb{i}", SessionId = session.Id, TaskId = 0, Type = ExerciseType.GapType,
                FormAsked = FormAsked.V2, AnswerGiven = "x", Outcome = AttemptOutcome.Wrong, CreatedAt = Now.AddMinutes(i),
            });
            await db.SaveChangesAsync();
        }

        var candidates = await new VerbProgressService(db).MistakeCandidatesAsync(1);

        Assert.Equal(3, candidates.Count);
        Assert.DoesNotContain("verb0", candidates);
    }

    [Fact]
    public async Task Active_session_reports_progress_of_the_open_session_only()
    {
        await using var db = NewContext();
        var open = new VerbSession { UserId = 1, Mode = SessionMode.Learn, Family = "same", StartedAt = Now };
        db.VerbSessions.Add(open);
        await db.SaveChangesAsync();

        db.VerbTasks.Add(new VerbTask { SessionId = open.Id, Order = 0, Type = ExerciseType.Card, Verb = "cut", FormAsked = FormAsked.Recognition, Level = 1, Outcome = AttemptOutcome.Correct });
        db.VerbTasks.Add(new VerbTask { SessionId = open.Id, Order = 1, Type = ExerciseType.Card, Verb = "put", FormAsked = FormAsked.Recognition, Level = 1 });
        await db.SaveChangesAsync();

        var view = await new VerbProgressService(db).GetAsync(1);

        Assert.NotNull(view.ActiveSession);
        Assert.Equal(open.Id, view.ActiveSession.Id);
        Assert.Equal(1, view.ActiveSession.Answered);
        Assert.Equal(2, view.ActiveSession.Total);
    }
}
