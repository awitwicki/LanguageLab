using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class ExplanationsTests
{
    private static IrregularVerb Verb(string v1) => IrregularVerbCatalog.Find(v1)!;

    [Fact]
    public void Group_1_says_the_verb_does_not_change()
    {
        Assert.Equal("cut doesn't change: cut – cut – cut.", Explanations.For(Verb("cut"), FormAsked.V2, Tense.Past, null));
    }

    [Fact]
    public void Group_2_contrasts_the_perfect_with_the_past()
    {
        Assert.Equal(
            "After have / has it's come, not came: come – came – come.",
            Explanations.For(Verb("come"), FormAsked.V3, Tense.Perfect, ErrorKind.V2ForV3));
        Assert.Equal(
            "come goes back to its first form: come – came – come.",
            Explanations.For(Verb("come"), FormAsked.V2, Tense.Past, null));
    }

    [Fact]
    public void Group_3_says_the_two_forms_are_the_same()
    {
        Assert.Equal("V2 and V3 are the same: buy – bought – bought.", Explanations.For(Verb("buy"), FormAsked.Both, null, null));
    }

    [Fact]
    public void Group_4_names_the_form_that_was_asked()
    {
        Assert.Equal("Past Simple is went: go – went – gone.", Explanations.For(Verb("go"), FormAsked.V2, Tense.Past, null));
        Assert.Equal("After have / has it's gone, not went: go – went – gone.", Explanations.For(Verb("go"), FormAsked.V3, null, null));
    }

    [Fact]
    public void An_ed_mistake_gets_a_prefix_and_a_note_is_appended()
    {
        Assert.Equal(
            "No -ed here. read doesn't change: read – read – read. Spelt the same in all three forms, but V2 and V3 are pronounced /red/.",
            Explanations.For(Verb("read"), FormAsked.V2, Tense.Past, ErrorKind.EdSuffix));
    }
}
