using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class ExerciseFactoryTests
{
    private static IrregularVerb Verb(string v1) => IrregularVerbCatalog.Find(v1)!;

    [Fact]
    public void Gap_choice_removes_the_form_from_the_example_and_offers_four_options()
    {
        var spec = ExerciseFactory.GapChoice(Verb("buy"), Tense.Past, 2, new Random(3));

        Assert.Equal(ExerciseType.GapChoice, spec.Type);
        Assert.Equal(FormAsked.V2, spec.FormAsked);
        Assert.Equal("She ___ a new phone last week.", spec.Payload.Sentence);
        Assert.Equal(Tense.Past, spec.Payload.Tense);
        Assert.Equal("bought", spec.Payload.Correct);
        Assert.Equal(4, spec.Payload.Options!.Count);
        Assert.Contains("bought", spec.Payload.Options);
        Assert.Contains("buyed", spec.Payload.Options);
    }

    [Fact]
    public void Odd_one_shows_three_unchanging_forms_and_one_from_another_group()
    {
        var spec = ExerciseFactory.OddOne(Verb("cut"), 2, new Random(3))!;

        Assert.Equal(4, spec.Payload.Options!.Count);
        Assert.Contains("cut", spec.Payload.Options);
        Assert.Equal(3, spec.Payload.Options.Count(o => IrregularVerbCatalog.VerbsOfGroup(1).Any(v => v.V2[0] == o)));
        Assert.Contains(spec.Payload.Correct, spec.Payload.Options);
        Assert.NotEqual(1, Verb(spec.Payload.Hint!).Group);
        Assert.Equal(Verb(spec.Payload.Hint!).V2[0], spec.Payload.Correct);
        Assert.Null(ExerciseFactory.OddOne(Verb("go"), 2, new Random(3)));
    }

    [Fact]
    public void Match_pairs_the_anchor_with_its_family_up_to_five_and_shuffles_the_right_side()
    {
        var spec = ExerciseFactory.Match(Verb("think"), FormAsked.V2, 2, new Random(3))!;

        Assert.Equal(FormAsked.V2, spec.Payload.Form);
        Assert.Equal(["think", "catch", "teach", "buy", "bring"], spec.Payload.Pairs!.Select(p => p.Left));
        Assert.Equal(["thought", "caught", "taught", "bought", "brought"], spec.Payload.Pairs.Select(p => p.Right));
        Assert.Equal(spec.Payload.Pairs.Select(p => p.Right).OrderBy(r => r), spec.Payload.RightOrder!.OrderBy(r => r));
        Assert.Empty(spec.Payload.Matched!);
    }

    [Fact]
    public void Match_is_impossible_for_a_family_of_two_and_joins_alternative_forms()
    {
        Assert.Null(ExerciseFactory.Match(Verb("spend"), FormAsked.V2, 2, new Random(3)));

        var core = ExerciseFactory.Match(Verb("be"), FormAsked.V2, 2, new Random(3))!;
        Assert.Equal(3, core.Payload.Pairs!.Count);
        Assert.Equal("was / were", core.Payload.Pairs[0].Right);
    }

    [Fact]
    public void Form_pick_keeps_the_marked_form_and_is_impossible_when_the_forms_coincide()
    {
        var spec = ExerciseFactory.FormPick(Verb("go"), 3, new Random(3))!;

        Assert.Contains("[", spec.Payload.Sentence);
        Assert.Equal(spec.Payload.Tense == Tense.Past ? "v2" : "v3", spec.Payload.Correct);
        Assert.Null(ExerciseFactory.FormPick(Verb("buy"), 2, new Random(3)));
    }

    [Fact]
    public void Gap_type_carries_the_hint_and_triple_type_autofills_for_group_3_below_learning_3()
    {
        var gap = ExerciseFactory.GapType(Verb("go"), Tense.Perfect, 3);
        Assert.Equal("She has ___ to the shop.", gap.Payload.Sentence);
        Assert.Equal("go", gap.Payload.Hint);
        Assert.Equal(FormAsked.V3, gap.FormAsked);

        Assert.True(ExerciseFactory.TripleType(Verb("buy"), VerbState.Learning2, 3).Payload.AutofillV3);
        Assert.False(ExerciseFactory.TripleType(Verb("buy"), VerbState.Learning3, 3).Payload.AutofillV3);
        Assert.False(ExerciseFactory.TripleType(Verb("go"), VerbState.Learning2, 3).Payload.AutofillV3);
    }

    [Fact]
    public void Group_4_level_2_alternates_phases_by_appearance()
    {
        Assert.Equal([ExerciseType.Match, ExerciseType.GapChoice], Menus.For(4, 2, 0).Select(m => m.Type));
        Assert.Equal(FormAsked.V2, Menus.For(4, 2, 0)[0].Form);
        Assert.Equal(FormAsked.V3, Menus.For(4, 2, 1)[0].Form);
        Assert.Equal(Tense.Perfect, Menus.For(4, 2, 3)[1].Tense);
    }
}
