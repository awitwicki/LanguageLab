using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class VerbDrillServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (VerbDrillService Drill, VerbKnowledgeService Knowledge) Services(ApplicationDbContext db)
    {
        var knowledge = new VerbKnowledgeService(db);

        return (new VerbDrillService(knowledge), knowledge);
    }

    private static async Task PassAsync(VerbKnowledgeService knowledge, string verb, int group)
    {
        for (var i = 0; i < VerbScoring.PassStreak; i++)
        {
            await knowledge.ApplyAsync(1, verb, PromptForm.V1, true, 500, DrillMode.Batch, group, Now);
        }
    }

    [Fact]
    public async Task A_fresh_stage_serves_its_first_verb_with_the_whole_answer()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);

        var card = await drill.NextAsync(1, new DrillRequest(DrillMode.Batch, 1, DrillScope.Stage, null), new Random(1));

        Assert.NotNull(card);
        Assert.Equal("cut", card.Verb.V1);
        Assert.Equal("cut", card.Verb.V2);
        Assert.Equal("різати", card.Verb.Translation);
        Assert.Equal(1, card.Verb.Group);
        Assert.Equal(new ScopeProgressView(0, 9), card.Scope);
    }

    [Fact]
    public async Task The_prompt_form_picks_the_example_of_its_own_tense()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);
        var request = new DrillRequest(DrillMode.Batch, 4, DrillScope.Stage, null);

        // Many draws so every one of the three forms comes up at least once.
        var seen = new Dictionary<PromptForm, ExampleView>();

        for (var i = 0; i < 60; i++)
        {
            var card = (await drill.NextAsync(1, request, new Random(i)))!;
            seen[card.PromptForm] = card.Example;

            var expected = card.PromptForm switch
            {
                PromptForm.V1 => Tense.Present,
                PromptForm.V2 => Tense.Past,
                _ => Tense.Perfect,
            };

            Assert.Equal(expected, card.Example.Tense);
            Assert.Contains('[', card.Example.Text);
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public async Task Batch_mode_serves_nothing_once_the_stage_has_passed()
    {
        await using var db = NewContext();
        var (drill, knowledge) = Services(db);

        foreach (var verb in IrregularVerbCatalog.VerbsOfGroup(2))
        {
            await PassAsync(knowledge, verb.V1, 2);
        }

        var card = await drill.NextAsync(1, new DrillRequest(DrillMode.Batch, 2, DrillScope.Stage, null), new Random(1));

        Assert.Null(card);
    }

    [Fact]
    public async Task Batch_mode_stays_inside_the_five_word_window()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);
        var window = IrregularVerbCatalog.VerbsOfGroup(3).Take(BatchWindow.Size).Select(v => v.V1).ToList();

        for (var i = 0; i < 20; i++)
        {
            var card = (await drill.NextAsync(1, new DrillRequest(DrillMode.Batch, 3, DrillScope.Stage, null), new Random(i)))!;
            Assert.Contains(card.Verb.V1, window);
        }
    }

    [Fact]
    public async Task Scope_progress_counts_the_passed_verbs_of_the_set_drawn_from()
    {
        await using var db = NewContext();
        var (drill, knowledge) = Services(db);
        await PassAsync(knowledge, "cut", 1);

        var batch = await drill.NextAsync(1, new DrillRequest(DrillMode.Batch, 1, DrillScope.Stage, null), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 9), batch!.Scope);

        var cumulative = await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 2, DrillScope.Cumulative, null), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 12), cumulative!.Scope);

        var all = await drill.NextAsync(1, new DrillRequest(DrillMode.Free, null, DrillScope.All, null), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 68), all!.Scope);
    }

    [Fact]
    public async Task A_free_run_over_one_stage_never_leaves_it()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);
        var stage = IrregularVerbCatalog.VerbsOfGroup(1).Select(v => v.V1).ToList();

        for (var i = 0; i < 40; i++)
        {
            var card = (await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 1, DrillScope.Stage, null), new Random(i)))!;
            Assert.Contains(card.Verb.V1, stage);
        }
    }

    [Fact]
    public async Task A_cumulative_run_reaches_the_earlier_stages_but_no_later_one()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);
        var allowed = IrregularVerbCatalog.VerbsUpToGroup(2).Select(v => v.V1).ToList();
        var groups = new HashSet<int>();

        for (var i = 0; i < 120; i++)
        {
            var card = (await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 2, DrillScope.Cumulative, null), new Random(i)))!;
            Assert.Contains(card.Verb.V1, allowed);
            groups.Add(card.Verb.Group);
        }

        Assert.Equal([1, 2], groups.OrderBy(g => g));
    }

    [Fact]
    public async Task A_free_run_keeps_serving_verbs_the_learner_already_passed()
    {
        await using var db = NewContext();
        var (drill, knowledge) = Services(db);

        foreach (var verb in IrregularVerbCatalog.VerbsOfGroup(2))
        {
            await PassAsync(knowledge, verb.V1, 2);
        }

        var card = await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 2, DrillScope.Stage, null), new Random(1));

        Assert.NotNull(card);
    }

    [Fact]
    public async Task The_excluded_verb_is_skipped_when_the_scope_has_room()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);

        for (var i = 0; i < 40; i++)
        {
            var card = (await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 1, DrillScope.Stage, "cut"), new Random(i)))!;
            Assert.NotEqual("cut", card.Verb.V1);
        }
    }

    [Fact]
    public async Task A_verb_with_a_note_carries_it_to_the_card()
    {
        await using var db = NewContext();
        var (drill, _) = Services(db);
        var read = IrregularVerbCatalog.Find("read")!;

        // "read" is in stage 1; draw until it comes up in a free run over that stage.
        CardVerbView? card = null;

        for (var i = 0; i < 400 && card == null; i++)
        {
            var drawn = (await drill.NextAsync(1, new DrillRequest(DrillMode.Free, 1, DrillScope.Stage, null), new Random(i)))!;
            card = drawn.Verb.V1 == "read" ? drawn.Verb : null;
        }

        Assert.NotNull(card);
        Assert.Equal(read.Note, card.Note);
    }
}
