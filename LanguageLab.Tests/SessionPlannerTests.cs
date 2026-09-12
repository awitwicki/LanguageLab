using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class SessionPlannerTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, VerbProgress> Progress(params (string Verb, VerbState State)[] rows) =>
        rows.ToDictionary(r => r.Verb, r => new VerbProgress { UserId = 1, Verb = r.Verb, State = r.State, LastSeenAt = T0 }, StringComparer.Ordinal);

    private static Dictionary<string, VerbProgress> Family(string family, VerbState state) =>
        Progress(IrregularVerbCatalog.VerbsOf(family).Select(v => (v.V1, state)).ToArray());

    private static IReadOnlyList<TaskSpec> Learn(string family, Dictionary<string, VerbProgress> progress, int seed = 1) =>
        SessionPlanner.Plan(new PlanRequest(SessionMode.Learn, family, []), progress, new Random(seed));

    [Theory]
    [InlineData(3, 9)]
    [InlineData(5, 15)]
    [InlineData(9, 16)]
    [InlineData(2, 8)]
    public void Learn_size_is_three_per_verb_within_bounds(int verbs, int size)
    {
        Assert.Equal(size, SessionPlanner.LearnSize(verbs));
    }

    [Fact]
    public void A_fresh_family_starts_with_a_card_per_verb_then_level_2_in_menu_order()
    {
        var plan = Learn("same", Progress());

        Assert.Equal(16, plan.Count);
        Assert.All(plan.Take(9), t => Assert.Equal(ExerciseType.Card, t.Type));
        Assert.Equal(IrregularVerbCatalog.VerbsOf("same").Select(v => v.V1), plan.Take(9).Select(t => t.Verb.V1));
        Assert.All(plan.Skip(9), t => Assert.Equal(2, t.Level));
        Assert.Equal(ExerciseType.GapChoice, plan[9].Type);
        Assert.Equal(Tense.Past, plan[9].Payload.Tense);
        Assert.Equal("cut", plan[9].Verb.V1);
    }

    [Fact]
    public void Forgotten_verbs_come_first_and_learning_2_verbs_get_level_3_tasks()
    {
        var progress = Family("back", VerbState.Learning2);
        progress["run"].State = VerbState.Forgotten;

        var plan = Learn("back", progress);

        Assert.Equal("run", plan[0].Verb.V1);
        Assert.Equal(["run", "come", "become"], plan.Take(3).Select(t => t.Verb.V1));
        Assert.All(plan, t => Assert.Equal(3, t.Level));
        Assert.Contains(plan, t => t.Type == ExerciseType.GapType);
    }

    [Fact]
    public void Group_3_at_level_2_has_one_match_and_four_option_gap_choices()
    {
        var plan = Learn("ought", Family("ought", VerbState.Learning1));

        Assert.Equal(15, plan.Count);
        Assert.Equal(1, plan.Count(t => t.Type == ExerciseType.Match));
        Assert.All(plan.Where(t => t.Type == ExerciseType.GapChoice), t => Assert.Equal(4, t.Payload.Options!.Count));
        Assert.All(plan, t => Assert.Equal(2, t.Level));
    }

    [Fact]
    public void Group_4_at_level_2_alternates_past_and_perfect_and_matches_each_form_once()
    {
        var plan = Learn("i-a-u", Family("i-a-u", VerbState.Learning1));

        var matches = plan.Where(t => t.Type == ExerciseType.Match).ToList();
        Assert.Equal(2, matches.Count);
        Assert.Equal([FormAsked.V2, FormAsked.V3], matches.Select(m => m.FormAsked).OrderBy(f => f));

        var begin = plan.Where(t => t.Verb.V1 == "begin").ToList();
        Assert.Equal(3, begin.Count);
        Assert.Equal([FormAsked.V2, FormAsked.V3, FormAsked.V2], begin.Select(t => t.FormAsked));
    }

    [Fact]
    public void Group_2_has_form_pick_but_no_match_and_a_family_of_two_has_no_match_either()
    {
        var back = Learn("back", Family("back", VerbState.Learning1));
        Assert.DoesNotContain(back, t => t.Type == ExerciseType.Match);
        Assert.Contains(back, t => t.Type == ExerciseType.FormPick);

        var ent = Learn("ent", Family("ent", VerbState.Learning1));
        Assert.Equal(8, ent.Count);
        Assert.DoesNotContain(ent, t => t.Type == ExerciseType.Match);
    }

    [Fact]
    public void Odd_one_is_capped_at_two_per_session()
    {
        var plan = Learn("same", Family("same", VerbState.Learning1));

        Assert.Equal(16, plan.Count);
        Assert.Equal(2, plan.Count(t => t.Type == ExerciseType.OddOne));
    }

    [Fact]
    public void Group_3_level_3_autofills_the_triple_until_learning_3()
    {
        var learning2 = Learn("ought", Family("ought", VerbState.Learning2));
        Assert.All(learning2.Where(t => t.Type == ExerciseType.TripleType), t => Assert.True(t.Payload.AutofillV3));

        var learning3 = Learn("ought", Family("ought", VerbState.Learning3));
        Assert.All(learning3.Where(t => t.Type == ExerciseType.TripleType), t => Assert.False(t.Payload.AutofillV3));
    }

    [Fact]
    public void Errors_only_trains_the_given_verbs_twice_each()
    {
        var progress = Family("ought", VerbState.Learning2);
        var plan = SessionPlanner.Plan(
            new PlanRequest(SessionMode.ErrorsOnly, null, ["think", "teach", "buy", "bring", "catch"]), progress, new Random(1));

        Assert.Equal(10, plan.Count);
        Assert.All(IrregularVerbCatalog.VerbsOf("ought"), v => Assert.Equal(2, plan.Count(t => t.Verb.V1 == v.V1)));
        Assert.All(plan, t => Assert.Equal(3, t.Level));
    }

    [Fact]
    public void Errors_only_tops_up_to_five_verbs_with_the_weakest_started_ones()
    {
        var progress = Family("ought", VerbState.Learning3);
        progress["buy"].Streak = 5;
        progress["bring"].Streak = 0;
        progress["think"].Streak = 1;
        progress["catch"].Streak = 2;
        progress["teach"].Streak = 4;

        foreach (var (verb, row) in Family("same", VerbState.Learning1))
        {
            row.Streak = 9;
            progress[verb] = row;
        }

        var plan = SessionPlanner.Plan(new PlanRequest(SessionMode.ErrorsOnly, null, ["cut", "put"]), progress, new Random(1));

        var verbs = plan.Select(t => t.Verb.V1).Distinct().ToList();
        Assert.Equal(5, verbs.Count);
        Assert.Contains("cut", verbs);
        Assert.Contains("put", verbs);
        Assert.Equal(["bring", "think", "catch"], verbs.Where(v => IrregularVerbCatalog.Find(v)!.Family == "ought"));
    }

    [Fact]
    public void Errors_only_with_nothing_started_is_empty()
    {
        Assert.Empty(SessionPlanner.Plan(new PlanRequest(SessionMode.ErrorsOnly, null, []), Progress(), new Random(1)));
    }

    [Fact]
    public void Mixed_takes_ten_distinct_started_verbs_weakest_state_first()
    {
        var progress = Family("same", VerbState.Learned);

        foreach (var (verb, row) in Family("ought", VerbState.Learning1))
        {
            progress[verb] = row;
        }

        progress["cost"].State = VerbState.Forgotten;

        var plan = SessionPlanner.Plan(new PlanRequest(SessionMode.Mixed, null, []), progress, new Random(1));

        Assert.Equal(10, plan.Count);
        Assert.Equal(10, plan.Select(t => t.Verb.V1).Distinct().Count());
        Assert.Equal("cost", plan[0].Verb.V1);
        Assert.Equal(3, plan[0].Level);
        Assert.All(plan.Skip(1).Take(5), t => Assert.Equal("ought", t.Verb.Family));
        Assert.Empty(SessionPlanner.Plan(new PlanRequest(SessionMode.Mixed, null, []), Progress(), new Random(1)));
    }

    [Fact]
    public void Returns_are_two_single_verb_tasks_at_the_same_level()
    {
        var go = IrregularVerbCatalog.Find("go")!;

        var returns = SessionPlanner.Returns(go, VerbState.Learning1, 2, appearance: 1, new Random(1));

        Assert.Equal(2, returns.Count);
        Assert.All(returns, t => Assert.Equal("go", t.Verb.V1));
        Assert.All(returns, t => Assert.Equal(2, t.Level));
        Assert.All(returns, t => Assert.False(t.IsMultiVerb));
    }
}
