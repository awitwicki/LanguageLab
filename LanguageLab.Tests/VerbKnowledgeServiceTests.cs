using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class VerbKnowledgeServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task An_untouched_learner_sees_every_verb_at_zero()
    {
        await using var db = NewContext();

        var view = await new VerbKnowledgeService(db).GetAsync(1);

        Assert.Equal(0, view.LearnedPercent);
        Assert.Equal(68, view.Verbs.Count);
        Assert.Equal(4, view.Stages.Count);
        Assert.Equal([9, 3, 29, 27], view.Stages.Select(s => s.Total));
        Assert.All(view.Stages, s => Assert.Equal(0, s.Passed));
        Assert.All(view.Stages, s => Assert.Equal(0, s.Mastery));

        var cut = view.Verbs.Single(v => v.V1 == "cut");
        Assert.Equal(1, cut.Group);
        Assert.Equal("cut", cut.V2);
        Assert.Equal("різати", cut.Translation);
        Assert.False(cut.Passed);
        Assert.Equal(0, cut.Answers);
    }

    [Fact]
    public async Task Alternative_forms_are_joined_for_the_table()
    {
        await using var db = NewContext();

        var be = (await new VerbKnowledgeService(db).GetAsync(1)).Verbs.Single(v => v.V1 == "be");

        Assert.Equal("was / were", be.V2);
        Assert.Equal("been", be.V3);
    }

    [Fact]
    public async Task The_first_answer_creates_a_row_and_logs_the_click()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        var result = await service.ApplyAsync(1, "cut", PromptForm.V2, known: true, responseMs: 900,
            DrillMode.Batch, group: 1, Now);

        Assert.NotNull(result);
        Assert.Equal("cut", result.Verb);
        Assert.Equal(1.0, result.Mastery);
        Assert.Equal(1, result.Streak);
        Assert.False(result.Passed);

        var row = await db.VerbKnowledges.SingleAsync();
        Assert.Equal(1, row.Answers);
        Assert.Equal(1, row.Knows);
        Assert.Equal(Now, row.LastAnsweredAt);

        var logged = await db.VerbAnswers.SingleAsync();
        Assert.Equal(PromptForm.V2, logged.PromptForm);
        Assert.True(logged.Known);
        Assert.Equal(900, logged.ResponseMs);
        Assert.Equal(DrillMode.Batch, logged.Mode);
        Assert.Equal(1, logged.Group);
        Assert.Equal(Now, logged.CreatedAt);
    }

    [Fact]
    public async Task A_run_of_four_knows_passes_the_verb()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        for (var i = 0; i < 3; i++)
        {
            Assert.False((await service.ApplyAsync(1, "cut", PromptForm.V1, true, 800, DrillMode.Batch, 1, Now))!.Passed);
        }

        var fourth = await service.ApplyAsync(1, "cut", PromptForm.V1, true, 800, DrillMode.Batch, 1, Now);

        Assert.True(fourth!.Passed);
        Assert.Equal(4, fourth.Streak);
        Assert.Equal(4, (await db.VerbAnswers.CountAsync()));
    }

    [Fact]
    public async Task A_miss_resets_the_streak_and_drags_mastery_down()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        await service.ApplyAsync(1, "cut", PromptForm.V1, known: true, 500, DrillMode.Batch, 1, Now);
        var missed = await service.ApplyAsync(1, "cut", PromptForm.V1, known: false, 500, DrillMode.Batch, 1, Now);

        Assert.Equal(0, missed!.Streak);
        Assert.Equal(0.6, missed.Mastery, precision: 10);

        var row = await db.VerbKnowledges.SingleAsync();
        Assert.Equal(2, row.Answers);
        Assert.Equal(1, row.Knows);
    }

    /// <summary>Review focus 1: the clamp has to hold through the service too.</summary>
    [Fact]
    public async Task An_absurd_response_time_is_stored_clamped()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        var result = await service.ApplyAsync(1, "cut", PromptForm.V1, true, int.MaxValue, DrillMode.Free, null, Now);

        Assert.InRange(result!.Mastery, 0, 1);
        Assert.Equal(VerbScoring.MaxResponseMs, (await db.VerbAnswers.SingleAsync()).ResponseMs);
    }

    /// <summary>Review focus 3: a stale client naming a verb the catalog does not have.</summary>
    [Fact]
    public async Task An_unknown_verb_is_refused_and_writes_nothing()
    {
        await using var db = NewContext();

        var result = await new VerbKnowledgeService(db)
            .ApplyAsync(1, "walk", PromptForm.V1, true, 500, DrillMode.Batch, 1, Now);

        Assert.Null(result);
        Assert.Empty(await db.VerbKnowledges.ToListAsync());
        Assert.Empty(await db.VerbAnswers.ToListAsync());
    }

    [Fact]
    public async Task One_learner_never_sees_another_learners_progress()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        await service.ApplyAsync(1, "cut", PromptForm.V1, true, 500, DrillMode.Batch, 1, Now);

        var other = await service.GetAsync(2);

        Assert.Equal(0, other.Verbs.Single(v => v.V1 == "cut").Answers);
        Assert.Equal(1, (await service.GetAsync(1)).Verbs.Single(v => v.V1 == "cut").Answers);
    }

    [Fact]
    public async Task Stage_and_overall_progress_count_passed_verbs()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        foreach (var verb in new[] { "cut", "put" })
        {
            for (var i = 0; i < VerbScoring.PassStreak; i++)
            {
                await service.ApplyAsync(1, verb, PromptForm.V1, true, 500, DrillMode.Batch, 1, Now);
            }
        }

        var view = await service.GetAsync(1);
        var stage1 = view.Stages.Single(s => s.Group == 1);

        Assert.Equal(2, stage1.Passed);
        Assert.Equal(2 / 9.0, stage1.Mastery, precision: 10);
        Assert.Equal(3, view.LearnedPercent); // 2 of 68
    }

    [Fact]
    public async Task Standings_come_back_in_the_order_of_the_verbs_asked_for()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);
        await service.ApplyAsync(1, "put", PromptForm.V1, true, 500, DrillMode.Batch, 1, Now);

        var standings = await service.StandingsAsync(1, IrregularVerbCatalog.VerbsOfGroup(1));

        Assert.Equal(9, standings.Count);
        Assert.Equal(IrregularVerbCatalog.VerbsOfGroup(1).Select(v => v.V1), standings.Select(s => s.Verb));
        Assert.Equal(1, standings.Single(s => s.Verb == "put").Answers);
        Assert.Equal(0, standings.Single(s => s.Verb == "cut").Answers);
    }
}
