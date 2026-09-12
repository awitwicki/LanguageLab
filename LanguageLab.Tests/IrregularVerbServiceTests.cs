using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class IrregularVerbServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IrregularVerbFormProgress Row(long userId, string verb, VerbForm form, int streak) =>
        new() { UserId = userId, Verb = verb, Form = form, Streak = streak, Correct = streak, LastAnsweredAt = Now };

    [Fact]
    public async Task Overview_folds_form_states_and_counts_per_step()
    {
        await using var db = NewContext();
        db.IrregularVerbFormProgresses.AddRange(
            Row(1, "come", VerbForm.V1, 3), Row(1, "come", VerbForm.V2, 3), Row(1, "come", VerbForm.V3, 3),
            Row(1, "run", VerbForm.V2, 0), Row(1, "run", VerbForm.V3, 1));
        await db.SaveChangesAsync();

        var overview = await new IrregularVerbService(db).GetOverviewAsync(1);

        Assert.Equal([1, 2, 3, 4], overview.Steps.Select(s => s.Step));

        var step2 = overview.Steps[1];
        Assert.Equal("First and third alike", step2.Title);
        Assert.Equal(3, step2.TotalVerbs);
        Assert.Equal(1, step2.LearnedVerbs);
        Assert.Equal(9, step2.TotalForms);
        Assert.Equal(3, step2.LearnedForms);

        var come = step2.Verbs.Single(v => v.V1 == "come");
        Assert.True(come.Learned);
        Assert.All(come.Forms, f => Assert.Equal(FormState.Learned, f.State));

        var run = step2.Verbs.Single(v => v.V1 == "run");
        Assert.False(run.Learned);
        Assert.Equal("бігти", run.Translation);
        Assert.Equal([VerbForm.V1, VerbForm.V2, VerbForm.V3], run.Forms.Select(f => f.Form));
        Assert.Equal([FormState.Unseen, FormState.Missed, FormState.Learning], run.Forms.Select(f => f.State));
        Assert.Equal([0, 0, 1], run.Forms.Select(f => f.Streak));
    }

    [Fact]
    public async Task Overview_is_scoped_to_the_calling_user()
    {
        await using var db = NewContext();
        db.IrregularVerbFormProgresses.AddRange(
            Row(2, "come", VerbForm.V1, 3), Row(2, "come", VerbForm.V2, 3), Row(2, "come", VerbForm.V3, 3));
        await db.SaveChangesAsync();

        var overview = await new IrregularVerbService(db).GetOverviewAsync(1);

        Assert.Equal(0, overview.Steps[1].LearnedForms);
        Assert.All(overview.Steps[1].Verbs.SelectMany(v => v.Forms), f => Assert.Equal(FormState.Unseen, f.State));
    }

    [Fact]
    public async Task Session_returns_the_planned_cards_with_the_open_form()
    {
        await using var db = NewContext();
        db.IrregularVerbFormProgresses.AddRange(Row(1, "come", VerbForm.V2, 3), Row(1, "come", VerbForm.V3, 3));
        await db.SaveChangesAsync();

        var session = await new IrregularVerbService(db).GetSessionAsync(1, 2);

        Assert.NotNull(session);
        Assert.Equal(2, session.Step);
        Assert.Equal("First and third alike", session.Title);
        Assert.False(session.Review);
        Assert.Equal(["become", "run", "come"], session.Cards.Select(c => c.V1));

        var come = session.Cards.Single(c => c.V1 == "come");
        Assert.Equal(VerbForm.V2, come.Open);
        Assert.Equal(("came", "come", "приходити"), (come.V2, come.V3, come.Translation));
    }

    [Fact]
    public async Task Session_is_a_review_once_the_step_is_learned()
    {
        await using var db = NewContext();

        foreach (var verb in IrregularVerbTable.Steps[2])
        {
            db.IrregularVerbFormProgresses.AddRange(VerbProgress.AllForms.Select(f => Row(1, verb.V1, f, 3)));
        }

        await db.SaveChangesAsync();

        var session = await new IrregularVerbService(db).GetSessionAsync(1, 2);

        Assert.NotNull(session);
        Assert.True(session.Review);
        Assert.Equal(3, session.Cards.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Session_is_null_for_a_step_outside_1_to_4(int step)
    {
        await using var db = NewContext();

        Assert.Null(await new IrregularVerbService(db).GetSessionAsync(1, step));
    }

    [Fact]
    public async Task Grade_creates_the_form_row_on_the_first_grade()
    {
        await using var db = NewContext();

        var result = await new IrregularVerbService(db).GradeAsync(1, "go", VerbForm.V2, correct: true, Now);

        Assert.NotNull(result);
        Assert.Equal("go", result.Verb);
        Assert.False(result.Learned);
        Assert.Equal([FormState.Unseen, FormState.Learning, FormState.Unseen], result.Forms.Select(f => f.State));

        var row = await db.IrregularVerbFormProgresses.SingleAsync();
        Assert.Equal(
            (1L, "go", VerbForm.V2, 1, 1, 0, Now),
            (row.UserId, row.Verb, row.Form, row.Streak, row.Correct, row.Wrong, row.LastAnsweredAt));
    }

    [Fact]
    public async Task Grade_updates_the_same_row_on_a_later_grade_and_reports_learned()
    {
        await using var db = NewContext();
        db.IrregularVerbFormProgresses.AddRange(
            Row(1, "go", VerbForm.V1, 3), Row(1, "go", VerbForm.V2, 3), Row(1, "go", VerbForm.V3, 2));
        await db.SaveChangesAsync();

        var result = await new IrregularVerbService(db).GradeAsync(1, "go", VerbForm.V3, correct: true, Now);

        Assert.NotNull(result);
        Assert.True(result.Learned);
        Assert.Equal(3, await db.IrregularVerbFormProgresses.CountAsync());
        Assert.Equal(3, (await db.IrregularVerbFormProgresses.SingleAsync(p => p.Form == VerbForm.V3)).Streak);
    }

    [Fact]
    public async Task Grade_returns_null_for_a_verb_not_in_the_table()
    {
        await using var db = NewContext();

        Assert.Null(await new IrregularVerbService(db).GradeAsync(1, "walk", VerbForm.V2, correct: true, Now));
        Assert.Equal(0, await db.IrregularVerbFormProgresses.CountAsync());
    }
}
