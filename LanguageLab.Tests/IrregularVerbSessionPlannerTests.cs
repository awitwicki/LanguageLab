using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class IrregularVerbSessionPlannerTests
{
    private static readonly DateTime T1 = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    private static IrregularVerbFormProgress Row(string verb, VerbForm form, int streak, DateTime at) =>
        new() { UserId = 1, Verb = verb, Form = form, Streak = streak, LastAnsweredAt = at };

    /// <summary>All three forms at the same streak.</summary>
    private static IEnumerable<IrregularVerbFormProgress> Verb(string verb, int streak, DateTime at) =>
        VerbProgress.AllForms.Select(form => Row(verb, form, streak, at));

    private static IEnumerable<IrregularVerbFormProgress> Learned(string verb, DateTime at) => Verb(verb, 3, at);

    private static IReadOnlyList<string> Verbs(SessionPlan plan) => plan.Cards.Select(c => c.Verb.V1).ToList();

    [Fact]
    public void With_no_progress_the_first_ten_verbs_come_in_table_order_with_v1_open()
    {
        var plan = IrregularVerbSessionPlanner.Plan(3, []);

        Assert.False(plan.IsReview);
        Assert.Equal(IrregularVerbTable.Steps[3].Take(10).Select(v => v.V1), Verbs(plan));
        Assert.All(plan.Cards, c => Assert.Equal(VerbForm.V1, c.Open));
    }

    [Fact]
    public void A_small_step_is_taken_whole()
    {
        Assert.Equal(["come", "become", "run"], Verbs(IrregularVerbSessionPlanner.Plan(2, [])));
    }

    [Fact]
    public void Missed_verbs_come_first_then_unseen_then_in_progress_and_learned_are_left_out()
    {
        // Step 1 in table order: cut put let set hit shut cost hurt read.
        var rows = Learned("cut", T1)
            .Concat(Verb("put", 1, T1))
            .Concat([Row("set", VerbForm.V2, 0, T1), Row("set", VerbForm.V3, 3, T1)])
            .Concat(Verb("read", 2, T1))
            .ToList();

        var plan = IrregularVerbSessionPlanner.Plan(1, rows);

        Assert.False(plan.IsReview);
        Assert.Equal(["set", "let", "hit", "shut", "cost", "hurt", "put", "read"], Verbs(plan));
    }

    [Fact]
    public void In_progress_verbs_go_by_total_streak_then_by_oldest_grade()
    {
        var rows = Verb("cut", 2, T1)
            .Concat(Verb("put", 1, T2))
            .Concat(Verb("let", 1, T1))
            .ToList();

        var plan = IrregularVerbSessionPlanner.Plan(1, rows);

        Assert.Equal(["set", "hit", "shut", "cost", "hurt", "read", "let", "put", "cut"], Verbs(plan));
    }

    [Fact]
    public void The_open_form_is_the_strongest_one()
    {
        var rows = new[] { Row("come", VerbForm.V2, 3, T1), Row("come", VerbForm.V3, 3, T1) };

        var plan = IrregularVerbSessionPlanner.Plan(2, rows);

        Assert.Equal(["become", "run", "come"], Verbs(plan));
        Assert.Equal(VerbForm.V2, plan.Cards.Single(c => c.Verb.V1 == "come").Open);
    }

    [Fact]
    public void A_finished_step_becomes_a_review_of_the_longest_unanswered_verbs()
    {
        var rows = Learned("come", T2).Concat(Learned("become", T1)).Concat(Learned("run", T1)).ToList();

        var plan = IrregularVerbSessionPlanner.Plan(2, rows);

        Assert.True(plan.IsReview);
        Assert.Equal(["become", "run", "come"], Verbs(plan));
        Assert.All(plan.Cards, c => Assert.Equal(VerbForm.V1, c.Open));
    }

    [Fact]
    public void A_review_is_capped_at_the_session_size()
    {
        var rows = IrregularVerbTable.Steps[3].SelectMany(v => Learned(v.V1, T1)).ToList();

        var plan = IrregularVerbSessionPlanner.Plan(3, rows);

        Assert.True(plan.IsReview);
        Assert.Equal(IrregularVerbSessionPlanner.SessionSize, plan.Cards.Count);
        Assert.Equal(IrregularVerbTable.Steps[3].Take(10).Select(v => v.V1), Verbs(plan));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void An_out_of_range_step_throws(int step)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IrregularVerbSessionPlanner.Plan(step, []));
    }
}
