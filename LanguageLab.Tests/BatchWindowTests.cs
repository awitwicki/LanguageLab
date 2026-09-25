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
        Assert.Null(BatchWindow.Next(stage, exclude: null));
        Assert.Null(BatchWindow.Next([], exclude: null));
    }

    [Fact]
    public void The_weakest_verb_of_the_window_comes_next()
    {
        List<VerbStanding> stage = [Standing("a", 0.8), Standing("b", 0.2), Standing("c", 0.5)];

        Assert.Equal("b", BatchWindow.Next(stage, exclude: null)!.Verb);
    }

    [Fact]
    public void Equal_mastery_is_broken_by_fewest_answers_then_catalog_order()
    {
        List<VerbStanding> byAnswers = [Standing("a", 0.5, answers: 6), Standing("b", 0.5, answers: 2)];
        Assert.Equal("b", BatchWindow.Next(byAnswers, exclude: null)!.Verb);

        List<VerbStanding> byOrder = [Standing("a", 0.5, answers: 3), Standing("b", 0.5, answers: 3)];
        Assert.Equal("a", BatchWindow.Next(byOrder, exclude: null)!.Verb);
    }

    [Fact]
    public void A_new_verb_outranks_a_practised_one_of_the_same_mastery()
    {
        List<VerbStanding> stage = [Standing("a", 0, answers: 4), New("b")];

        Assert.Equal("b", BatchWindow.Next(stage, exclude: null)!.Verb);
    }

    [Fact]
    public void The_verb_just_answered_is_not_served_twice_in_a_row()
    {
        List<VerbStanding> stage = [Standing("a", 0.1), Standing("b", 0.9)];

        Assert.Equal("b", BatchWindow.Next(stage, exclude: "a")!.Verb);
    }

    /// <summary>Review focus 5: with one word left the drill must not dead-end.</summary>
    [Fact]
    public void The_last_unpassed_verb_is_served_even_when_it_is_the_one_just_answered()
    {
        List<VerbStanding> stage = [Passed("a"), Standing("b", 0.3), Passed("c")];

        Assert.Equal("b", BatchWindow.Next(stage, exclude: "b")!.Verb);
    }
}
