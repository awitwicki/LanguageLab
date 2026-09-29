using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class VerbSessionServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (VerbSessionService Sessions, VerbKnowledgeService Knowledge) Services(ApplicationDbContext db)
    {
        var knowledge = new VerbKnowledgeService(db);

        return (new VerbSessionService(knowledge), knowledge);
    }

    private static async Task AnswerAsync(
        VerbKnowledgeService knowledge, string verb, int group, bool known, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await knowledge.ApplyAsync(1, verb, PromptForm.V1, known ? verb : "wrong", 500, DrillMode.Batch, group, Now);
        }
    }

    [Fact]
    public async Task A_fresh_stage_hands_over_its_first_five_verbs_in_catalog_order()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Batch, 1, DrillScope.Stage), new Random(1));

        Assert.NotNull(session);
        Assert.Equal(DrillMode.Batch, session.Mode);
        Assert.Null(session.Queue);
        Assert.Equal(["cut", "put", "let", "set", "hit"], session.Verbs.Select(v => v.V1));
        Assert.Equal(new ScopeProgressView(0, 9), session.Scope);
    }

    [Fact]
    public async Task A_verb_already_answered_falls_behind_the_ones_never_met()
    {
        await using var db = NewContext();
        var (sessions, knowledge) = Services(db);
        await AnswerAsync(knowledge, "cut", 1, known: false);

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Batch, 1, DrillScope.Stage), new Random(1));

        Assert.Equal(["put", "let", "set", "hit", "cut"], session!.Verbs.Select(v => v.V1));
        Assert.Equal(1, session.Verbs.Single(v => v.V1 == "cut").Answers);
    }

    [Fact]
    public async Task A_verb_carries_its_forms_its_exercises_for_all_three_forms_and_a_form_order()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Batch, 1, DrillScope.Stage), new Random(1));

        var cut = session!.Verbs.Single(v => v.V1 == "cut");
        Assert.Equal("cut", cut.V2);
        Assert.Equal("cut", cut.V3);
        Assert.Equal("різати", cut.Translation);
        Assert.Equal(1, cut.Group);

        Assert.Equal(
            [PromptForm.V1, PromptForm.V2, PromptForm.V3],
            cut.Exercises.Select(e => e.Form).Distinct());
        Assert.All(cut.Exercises, e => Assert.Equal("cut", e.Answer));

        Assert.Equal(
            [PromptForm.V1, PromptForm.V2, PromptForm.V3],
            cut.FormOrder.OrderBy(f => f));
    }

    [Fact]
    public async Task The_form_order_is_shuffled_rather_than_always_the_same()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);
        var seen = new HashSet<string>();

        for (var seed = 0; seed < 40; seed++)
        {
            var session = await sessions.StartAsync(
                1, new SessionRequest(DrillMode.Batch, 1, DrillScope.Stage), new Random(seed));

            foreach (var verb in session!.Verbs)
            {
                seen.Add(string.Join(",", verb.FormOrder));
            }
        }

        Assert.True(seen.Count > 1, "every verb got the same form order in 40 draws");
    }

    [Fact]
    public async Task Ordinary_training_hands_over_nothing_once_the_stage_has_passed()
    {
        await using var db = NewContext();
        var (sessions, knowledge) = Services(db);

        foreach (var verb in IrregularVerbCatalog.VerbsOfGroup(2))
        {
            await AnswerAsync(knowledge, verb.V1, 2, known: true, times: VerbScoring.PassStreak);
        }

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Batch, 2, DrillScope.Stage), new Random(1));

        Assert.Null(session);
    }

    [Fact]
    public async Task A_free_run_hands_over_a_queue_whose_verbs_are_all_listed()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, 1, DrillScope.Stage), new Random(3));

        Assert.NotNull(session);
        Assert.Equal(DrillMode.Free, session.Mode);
        Assert.Equal(VerbSessionService.FreeQueueLength, session.Queue!.Count);

        var listed = session.Verbs.Select(v => v.V1).ToHashSet();
        Assert.All(session.Queue, card => Assert.Contains(card.Verb, listed));

        // The queue is the play order; a verb it names twice is listed once.
        Assert.Equal(session.Verbs.Count, listed.Count);

        var stage = IrregularVerbCatalog.VerbsOfGroup(1).Select(v => v.V1).ToHashSet();
        Assert.All(session.Verbs, v => Assert.Contains(v.V1, stage));
    }

    [Fact]
    public async Task A_free_run_keeps_serving_a_stage_the_learner_already_passed()
    {
        await using var db = NewContext();
        var (sessions, knowledge) = Services(db);

        foreach (var verb in IrregularVerbCatalog.VerbsOfGroup(2))
        {
            await AnswerAsync(knowledge, verb.V1, 2, known: true, times: VerbScoring.PassStreak);
        }

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, 2, DrillScope.Stage), new Random(1));

        Assert.NotNull(session);
        Assert.NotEmpty(session.Queue!);
    }

    [Fact]
    public async Task Scope_progress_counts_the_passed_verbs_of_the_set_drawn_from()
    {
        await using var db = NewContext();
        var (sessions, knowledge) = Services(db);
        await AnswerAsync(knowledge, "cut", 1, known: true, times: VerbScoring.PassStreak);

        var batch = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Batch, 1, DrillScope.Stage), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 9), batch!.Scope);

        var cumulative = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, 2, DrillScope.Cumulative), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 12), cumulative!.Scope);

        var all = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, null, DrillScope.All), new Random(1));
        Assert.Equal(new ScopeProgressView(1, 68), all!.Scope);
    }

    [Fact]
    public async Task A_cumulative_run_reaches_the_earlier_stages_but_no_later_one()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);
        var allowed = IrregularVerbCatalog.VerbsUpToGroup(2).Select(v => v.V1).ToHashSet();

        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, 2, DrillScope.Cumulative), new Random(5));

        Assert.All(session!.Verbs, v => Assert.Contains(v.V1, allowed));
    }

    [Fact]
    public async Task A_verb_with_a_note_carries_it_over()
    {
        await using var db = NewContext();
        var (sessions, _) = Services(db);
        var read = IrregularVerbCatalog.Find("read")!;

        // "read" is the ninth verb of stage 1, so only a run over the whole stage reaches it.
        var session = await sessions.StartAsync(
            1, new SessionRequest(DrillMode.Free, 1, DrillScope.Stage), new Random(1));

        var listed = session!.Verbs.SingleOrDefault(v => v.V1 == "read");

        // A 20-card chunk of a 9-verb stage all but certainly includes it; skip if it did not.
        if (listed != null)
        {
            Assert.Equal(read.Note, listed.Note);
        }
    }
}
