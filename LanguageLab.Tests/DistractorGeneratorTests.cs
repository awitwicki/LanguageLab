using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class DistractorGeneratorTests
{
    private static IrregularVerb Verb(string v1) => IrregularVerbCatalog.Find(v1)!;

    [Theory]
    [InlineData("cut", "cutted")]
    [InlineData("leave", "leaved")]
    [InlineData("fly", "flied")]
    [InlineData("say", "sayed")]
    [InlineData("go", "goed")]
    public void Ed_form_follows_regular_spelling(string v1, string expected)
    {
        Assert.Equal(expected, DistractorGenerator.EdForm(v1));
    }

    [Theory]
    [InlineData("cut", "cutted", true)]
    [InlineData("swim", "swimmed", true)]
    [InlineData("leave", "leaved", true)]
    [InlineData("fly", "flied", true)]
    [InlineData("cut", "cat", false)]
    [InlineData("go", "went", false)]
    public void Is_ed_form_recognises_every_regular_spelling(string v1, string answer, bool expected)
    {
        Assert.Equal(expected, DistractorGenerator.IsEdForm(Verb(v1), answer));
    }

    [Fact]
    public void Group_3_distractors_are_the_ed_form_and_a_family_neighbour_never_the_answer()
    {
        var options = DistractorGenerator.For(Verb("buy"), FormAsked.V2, 3, new Random(1));

        Assert.Equal(3, options.Count);
        Assert.Contains("buyed", options);
        Assert.Contains(options, o => new[] { "brought", "thought", "caught", "taught" }.Contains(o));
        Assert.DoesNotContain("bought", options);
        Assert.Equal(options.Distinct().Count(), options.Count);
    }

    [Fact]
    public void Group_4_perfect_distractors_include_the_past_simple_swap()
    {
        var options = DistractorGenerator.For(Verb("go"), FormAsked.V3, 3, new Random(1));

        Assert.Contains("went", options);
        Assert.Contains("goed", options);
        Assert.DoesNotContain("gone", options);
    }

    [Fact]
    public void Group_1_distractors_are_the_ed_form_a_confusable_and_a_form_from_another_group()
    {
        var options = DistractorGenerator.For(Verb("cut"), FormAsked.V2, 3, new Random(1));

        Assert.Equal(3, options.Count);
        Assert.Contains("cutted", options);
        Assert.Contains("cat", options);
        Assert.DoesNotContain("cut", options);
        Assert.DoesNotContain(options, o => IrregularVerbCatalog.VerbsOfGroup(1).Any(v => v.V1 == o));
        Assert.Contains(options, o => IrregularVerbCatalog.Verbs.Any(v => v.Group != 1 && v.V2.Contains(o)));
    }

    [Fact]
    public void Group_2_perfect_offers_the_past_simple_before_anything_else()
    {
        var options = DistractorGenerator.For(Verb("come"), FormAsked.V3, 2, new Random(1));

        Assert.Equal(["came", "comed"], options);
    }

    [Fact]
    public void Never_returns_an_accepted_alternative_form()
    {
        var options = DistractorGenerator.For(Verb("get"), FormAsked.V3, 4, new Random(1));

        Assert.DoesNotContain("got", options);
        Assert.DoesNotContain("gotten", options);
    }
}
