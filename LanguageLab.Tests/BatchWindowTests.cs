using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class BatchWindowTests
{
    private static VerbStanding New(string verb) => new(verb, 0, 0, 0);

    private static VerbStanding Passed(string verb) => new(verb, 0.9, VerbScoring.PassStreak, 4);

    private static VerbStanding Standing(string verb, double mastery, int streak = 1, int answers = 3) =>
        new(verb, mastery, streak, answers);

    private static List<VerbStanding> Stage(params string[] verbs) => verbs.Select(New).ToList();

    [Fact]
    public void The_window_is_the_first_five_unpassed_verbs_in_catalog_order()
    {
        var stage = Stage("a", "b", "c", "d", "e", "f", "g");

        Assert.Equal(["a", "b", "c", "d", "e"], BatchWindow.Of(stage).Select(s => s.Verb));
    }

    [Fact]
    public void Passed_verbs_are_skipped_so_the_window_slides_forward()
    {
        List<VerbStanding> stage = [Passed("a"), Passed("b"), New("c"), New("d"), New("e"), New("f"), New("g")];

        Assert.Equal(["c", "d", "e", "f", "g"], BatchWindow.Of(stage).Select(s => s.Verb));
    }

    [Fact]
    public void A_regressed_verb_re_enters_the_window_ahead_of_later_ones()
    {
        // "b" was passed and then missed: streak back to 0, so it is unpassed again and,
        // being earlier in catalog order, comes back first.
        List<VerbStanding> stage =
            [Passed("a"), Standing("b", 0.6, streak: 0), Passed("c"), Passed("d"), New("e"), New("f"), New("g")];

        Assert.Equal(["b", "e", "f", "g"], BatchWindow.Of(stage).Select(s => s.Verb));
    }

    [Fact]
    public void A_short_stage_is_a_single_window()
    {
        Assert.Equal(3, BatchWindow.Of(Stage("come", "become", "run")).Count);
    }

    [Fact]
    public void Nothing_is_served_once_every_verb_of_the_stage_has_passed()
    {
        List<VerbStanding> stage = [Passed("a"), Passed("b")];

        Assert.Empty(BatchWindow.Of(stage));
        Assert.Empty(BatchWindow.Of([]));
    }

    [Fact]
    public void The_window_is_the_first_five_verbs_that_have_not_passed()
    {
        List<VerbStanding> stage = [Passed("a"), New("b"), New("c"), New("d"), New("e"), New("f"), New("g")];

        Assert.Equal(["b", "c", "d", "e", "f"], BatchWindow.Of(stage).Select(s => s.Verb));
    }

    [Fact]
    public void A_never_answered_verb_comes_before_one_already_being_learned()
    {
        List<VerbStanding> stage =
            [Standing("seen", 0.9, streak: 3, answers: 6), New("fresh"), Standing("started", 0.5, streak: 1, answers: 2)];

        Assert.Equal(["fresh", "started", "seen"], BatchWindow.Ordered(stage).Select(s => s.Verb));
    }

    [Fact]
    public void Verbs_alike_in_standing_keep_their_catalog_order()
    {
        List<VerbStanding> stage = [New("a"), New("b"), New("c")];

        Assert.Equal(["a", "b", "c"], BatchWindow.Ordered(stage).Select(s => s.Verb));
    }

    [Fact]
    public void The_order_never_reaches_past_the_window()
    {
        // Streaks 1,2,3,0,1,2,3,0,1 — all below PassStreak, so the window is v1..v5, and the
        // order inside it is v4 (streak 0), v1 and v5 (streak 1, catalog order), v2, v3.
        var stage = Enumerable.Range(1, 9)
            .Select(i => Standing($"v{i}", 0, streak: i % 4, answers: 5))
            .ToList();

        var ordered = BatchWindow.Ordered(stage);

        Assert.Equal(BatchWindow.Size, ordered.Count);
        Assert.Equal(["v4", "v1", "v5", "v2", "v3"], ordered.Select(s => s.Verb));
    }
}
