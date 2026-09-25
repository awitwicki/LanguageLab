using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class FreePickerTests
{
    private static VerbStanding Weak(string verb) => new(verb, 0.2, 1, 5);

    private static VerbStanding Strong(string verb) => new(verb, 0.95, 6, 8);

    [Fact]
    public void A_verb_never_answered_counts_as_weak()
    {
        Assert.True(FreePicker.IsWeak(new VerbStanding("cut", 0, 0, 0)));
        // Even a high stored mastery with no answers behind it — a row cannot exist that way,
        // but the rule must not depend on that.
        Assert.True(FreePicker.IsWeak(new VerbStanding("cut", 1.0, 0, 0)));
    }

    [Fact]
    public void Weakness_is_mastery_below_the_threshold()
    {
        Assert.True(FreePicker.IsWeak(new VerbStanding("cut", VerbScoring.WeakBelow - 0.01, 1, 3)));
        Assert.False(FreePicker.IsWeak(new VerbStanding("cut", VerbScoring.WeakBelow, 1, 3)));
    }

    [Fact]
    public void An_empty_scope_serves_nothing()
    {
        Assert.Null(FreePicker.Next([], exclude: null, new Random(1)));
    }

    [Fact]
    public void A_scope_of_only_weak_verbs_always_draws_from_it()
    {
        List<VerbStanding> scope = [Weak("a"), Weak("b")];
        var random = new Random(7);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(FreePicker.IsWeak(FreePicker.Next(scope, exclude: null, random)!));
        }
    }

    [Fact]
    public void A_scope_of_only_strong_verbs_still_serves_cards()
    {
        List<VerbStanding> scope = [Strong("a"), Strong("b")];
        var random = new Random(7);

        for (var i = 0; i < 50; i++)
        {
            Assert.False(FreePicker.IsWeak(FreePicker.Next(scope, exclude: null, random)!));
        }
    }

    [Fact]
    public void Roughly_two_cards_in_three_come_from_the_weak_pool()
    {
        List<VerbStanding> scope = [Weak("a"), Weak("b"), Weak("c"), Strong("d"), Strong("e"), Strong("f")];
        var random = new Random(20260925);

        var weak = Enumerable.Range(0, 4000)
            .Count(_ => FreePicker.IsWeak(FreePicker.Next(scope, exclude: null, random)!));

        Assert.InRange(weak / 4000.0, FreePicker.WeakShare - 0.04, FreePicker.WeakShare + 0.04);
    }

    [Fact]
    public void Every_verb_of_the_scope_can_come_up()
    {
        List<VerbStanding> scope = [Weak("a"), Weak("b"), Strong("c"), Strong("d")];
        var random = new Random(3);

        var seen = Enumerable.Range(0, 500)
            .Select(_ => FreePicker.Next(scope, exclude: null, random)!.Verb)
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(["a", "b", "c", "d"], seen);
    }

    [Fact]
    public void The_verb_just_answered_is_skipped()
    {
        List<VerbStanding> scope = [Weak("a"), Weak("b")];
        var random = new Random(11);

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal("b", FreePicker.Next(scope, exclude: "a", random)!.Verb);
        }
    }

    /// <summary>Review focus 5: a one-verb scope must still serve its only verb.</summary>
    [Fact]
    public void A_single_verb_scope_repeats_rather_than_dead_ending()
    {
        List<VerbStanding> scope = [Weak("a")];

        Assert.Equal("a", FreePicker.Next(scope, exclude: "a", new Random(1))!.Verb);
    }
}
