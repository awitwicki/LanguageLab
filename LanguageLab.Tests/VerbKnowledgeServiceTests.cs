using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Internal;

namespace LanguageLab.Tests;

public class VerbKnowledgeServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    // The in-memory provider has no transactions and makes the warning an error, so a test
    // reaching ApplyManyAsync would throw where production simply commits.
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

        var result = await service.ApplyAsync(1, "cut", PromptForm.V2, chosen: "cut", responseMs: 900,
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
        Assert.Equal("cut", logged.Chosen);
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
            Assert.False((await service.ApplyAsync(1, "cut", PromptForm.V1, "cut", 800, DrillMode.Batch, 1, Now))!.Passed);
        }

        var fourth = await service.ApplyAsync(1, "cut", PromptForm.V1, "cut", 800, DrillMode.Batch, 1, Now);

        Assert.True(fourth!.Passed);
        Assert.Equal(4, fourth.Streak);
        Assert.Equal(4, (await db.VerbAnswers.CountAsync()));
    }

    [Fact]
    public async Task A_miss_resets_the_streak_and_drags_mastery_down()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        await service.ApplyAsync(1, "cut", PromptForm.V1, chosen: "cut", 500, DrillMode.Batch, 1, Now);
        var missed = await service.ApplyAsync(1, "cut", PromptForm.V1, chosen: "cutted", 500, DrillMode.Batch, 1, Now);

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

        var result = await service.ApplyAsync(1, "cut", PromptForm.V1, "cut", int.MaxValue, DrillMode.Free, null, Now);

        Assert.InRange(result!.Mastery, 0, 1);
        Assert.Equal(VerbScoring.MaxResponseMs, (await db.VerbAnswers.SingleAsync()).ResponseMs);
    }

    /// <summary>Review focus 3: a stale client naming a verb the catalog does not have.</summary>
    [Fact]
    public async Task An_unknown_verb_is_refused_and_writes_nothing()
    {
        await using var db = NewContext();

        var result = await new VerbKnowledgeService(db)
            .ApplyAsync(1, "walk", PromptForm.V1, "walk", 500, DrillMode.Batch, 1, Now);

        Assert.Null(result);
        Assert.Empty(await db.VerbKnowledges.ToListAsync());
        Assert.Empty(await db.VerbAnswers.ToListAsync());
    }

    [Fact]
    public async Task One_learner_never_sees_another_learners_progress()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        await service.ApplyAsync(1, "cut", PromptForm.V1, "cut", 500, DrillMode.Batch, 1, Now);

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
                await service.ApplyAsync(1, verb, PromptForm.V1, verb, 500, DrillMode.Batch, 1, Now);
            }
        }

        var view = await service.GetAsync(1);
        var stage1 = view.Stages.Single(s => s.Group == 1);

        Assert.Equal(2, stage1.Passed);
        Assert.Equal(2 / 9.0, stage1.Mastery, precision: 10);
        Assert.Equal(3, view.LearnedPercent); // 2 of 68
    }

    [Fact]
    public async Task The_server_judges_the_pick_itself()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);

        var result = await service.ApplyAsync(1, "go", PromptForm.V2, "gone", 500, DrillMode.Batch, 4, Now);

        Assert.Equal(0, result!.Streak);
        Assert.False(db.VerbAnswers.Single().Known);
        Assert.Equal("gone", db.VerbAnswers.Single().Chosen);
    }

    /// <summary>
    /// Found on review: two devices answering the same verb at once used to leave the
    /// counters permanently behind the log, because each one incremented the row from the
    /// value it had read. The row is derived from the log now, so a writer working off a
    /// stale copy still lands on what the log actually holds.
    /// </summary>
    [Fact]
    public async Task A_stale_writer_still_lands_on_what_the_log_holds()
    {
        var database = Guid.NewGuid().ToString();

        static ApplicationDbContext Context(string name) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

        await using var seed = Context(database);
        await new VerbKnowledgeService(seed).ApplyAsync(1, "cut", PromptForm.V1, "cut", 500, DrillMode.Batch, 1, Now);

        // One device reads the standing and holds it — EF hands this context its tracked
        // copy from here on, which is exactly how the second tab goes stale.
        await using var stale = Context(database);
        await new VerbKnowledgeService(stale).GetAsync(1);

        // Meanwhile the other device answers twice.
        await using var other = Context(database);
        await new VerbKnowledgeService(other).ApplyAsync(1, "cut", PromptForm.V1, "cut", 500, DrillMode.Batch, 1, Now);
        await new VerbKnowledgeService(other).ApplyAsync(1, "cut", PromptForm.V1, "cut", 500, DrillMode.Batch, 1, Now);

        var result = await new VerbKnowledgeService(stale)
            .ApplyAsync(1, "cut", PromptForm.V1, "cut", 500, DrillMode.Batch, 1, Now);

        await using var fresh = Context(database);
        Assert.Equal(4, await fresh.VerbAnswers.CountAsync());
        // Four answers in the log, so four in the standing — not the two the stale copy saw.
        Assert.Equal(4, result!.Streak);
        Assert.Equal(4, (await fresh.VerbKnowledges.SingleAsync(k => k.Verb == "cut")).Answers);
    }

    [Fact]
    public async Task Standings_come_back_in_the_order_of_the_verbs_asked_for()
    {
        await using var db = NewContext();
        var service = new VerbKnowledgeService(db);
        await service.ApplyAsync(1, "put", PromptForm.V1, "put", 500, DrillMode.Batch, 1, Now);

        var standings = await service.StandingsAsync(1, IrregularVerbCatalog.VerbsOfGroup(1));

        Assert.Equal(9, standings.Count);
        Assert.Equal(IrregularVerbCatalog.VerbsOfGroup(1).Select(v => v.V1), standings.Select(s => s.Verb));
        Assert.Equal(1, standings.Single(s => s.Verb == "put").Answers);
        Assert.Equal(0, standings.Single(s => s.Verb == "cut").Answers);
    }

    private static VerbAnswerToApply Answer(string verb, bool known) =>
        new(verb, PromptForm.V1, known ? verb : "wrong", 500, DrillMode.Batch, 1);

    [Fact]
    public async Task A_chunk_of_answers_is_applied_in_the_order_it_was_given()
    {
        await using var db = NewContext();
        var knowledge = new VerbKnowledgeService(db);

        var results = await knowledge.ApplyManyAsync(
            1,
            [Answer("cut", true), Answer("cut", true), Answer("cut", false), Answer("put", true)],
            Now);

        Assert.Equal(["cut", "cut", "cut", "put"], results.Select(r => r.Verb));

        // The last answer for "cut" was a miss, so its streak is back to zero however fast
        // the two before it came.
        Assert.Equal(0, results[2].Streak);
        Assert.Equal(1, results[3].Streak);

        var cut = (await knowledge.GetAsync(1)).Verbs.Single(v => v.V1 == "cut");
        Assert.Equal(3, cut.Answers);
        Assert.Equal(0, cut.Streak);
    }

    [Fact]
    public async Task A_chunk_passes_a_verb_exactly_as_four_separate_answers_would()
    {
        await using var db = NewContext();
        var knowledge = new VerbKnowledgeService(db);

        await knowledge.ApplyManyAsync(
            1,
            Enumerable.Repeat(Answer("cut", true), VerbScoring.PassStreak).ToList(),
            Now);

        var cut = (await knowledge.GetAsync(1)).Verbs.Single(v => v.V1 == "cut");
        Assert.True(cut.Passed);
        Assert.Equal(VerbScoring.PassStreak, cut.Answers);
    }

    [Fact]
    public async Task An_empty_chunk_writes_nothing_and_returns_nothing()
    {
        await using var db = NewContext();
        var knowledge = new VerbKnowledgeService(db);

        Assert.Empty(await knowledge.ApplyManyAsync(1, [], Now));
        Assert.All((await knowledge.GetAsync(1)).Verbs, v => Assert.Equal(0, v.Answers));
    }
}
