using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class AnswerCheckerTests
{
    private static IrregularVerb Verb(string v1) => IrregularVerbCatalog.Find(v1)!;

    private static CheckResult Typed(string v1, FormAsked form, string answer, int neutralSoFar = 0) =>
        AnswerChecker.Check(Verb(v1), form, answer, typed: true, neutralSoFar);

    private static CheckResult Chosen(string v1, FormAsked form, string answer) =>
        AnswerChecker.Check(Verb(v1), form, answer, typed: false, neutralSoFar: 0);

    [Theory]
    [InlineData("go", FormAsked.V2, "went")]
    [InlineData("go", FormAsked.V2, "  WENT ")]
    [InlineData("be", FormAsked.V2, "were")]
    [InlineData("be", FormAsked.V2, "was")]
    [InlineData("get", FormAsked.V3, "gotten")]
    [InlineData("cut", FormAsked.V3, "cut")]
    public void Accepts_every_listed_form_ignoring_case_and_spaces(string v1, FormAsked form, string answer)
    {
        var result = Typed(v1, form, answer);

        Assert.Equal(AttemptOutcome.Correct, result.Outcome);
        Assert.Null(result.Kind);
    }

    [Theory]
    [InlineData("cut", FormAsked.V2, "cutted", ErrorKind.EdSuffix)]
    [InlineData("swim", FormAsked.V3, "swimmed", ErrorKind.EdSuffix)]
    [InlineData("go", FormAsked.V3, "went", ErrorKind.V2ForV3)]
    [InlineData("go", FormAsked.V2, "gone", ErrorKind.V3ForV2)]
    [InlineData("drink", FormAsked.V2, "drunk", ErrorKind.V3ForV2)]
    [InlineData("buy", FormAsked.V2, "brought", ErrorKind.WrongFamily)]
    [InlineData("sell", FormAsked.V3, "told", ErrorKind.WrongFamily)]
    [InlineData("go", FormAsked.V2, "xyz", ErrorKind.Other)]
    public void Classifies_a_wrong_typed_answer_in_rule_order(string v1, FormAsked form, string answer, ErrorKind kind)
    {
        var result = Typed(v1, form, answer);

        Assert.Equal(AttemptOutcome.Wrong, result.Outcome);
        Assert.Equal(kind, result.Kind);
    }

    [Fact]
    public void A_near_miss_spelling_is_neutral_once_then_wrong()
    {
        var first = Typed("buy", FormAsked.V2, "boght");
        Assert.Equal(AttemptOutcome.Neutral, first.Outcome);
        Assert.Equal(ErrorKind.Spelling, first.Kind);

        var second = Typed("buy", FormAsked.V2, "boght", neutralSoFar: 1);
        Assert.Equal(AttemptOutcome.Wrong, second.Outcome);
        Assert.Equal(ErrorKind.Spelling, second.Kind);
    }

    [Fact]
    public void A_near_miss_in_a_choice_task_is_wrong_at_once()
    {
        var result = Chosen("cut", FormAsked.V2, "cat");

        Assert.Equal(AttemptOutcome.Wrong, result.Outcome);
        Assert.Equal(ErrorKind.Spelling, result.Kind);
    }

    [Fact]
    public void Normalized_answer_is_reported_back()
    {
        Assert.Equal("went", Typed("go", FormAsked.V2, "  Went ").Normalized);
        Assert.Equal("was were", AnswerChecker.Normalize(" Was   WERE "));
    }

    [Theory]
    [InlineData("go", "go", 0)]
    [InlineData("bought", "boght", 1)]
    [InlineData("went", "wnet", 2)]
    [InlineData("", "go", 2)]
    public void Levenshtein(string a, string b, int expected)
    {
        Assert.Equal(expected, AnswerChecker.Levenshtein(a, b));
    }
}
