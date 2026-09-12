using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class IrregularVerbTableTests
{
    [Fact]
    public void Every_verb_has_a_unique_key_and_a_translation()
    {
        var verbs = IrregularVerbTable.All;

        Assert.Equal(68, verbs.Count);
        Assert.Equal(verbs.Count, verbs.Select(v => v.V1).Distinct(StringComparer.Ordinal).Count());
        Assert.All(verbs, v => Assert.False(string.IsNullOrWhiteSpace(v.Translation), v.V1));
    }

    [Fact]
    public void Steps_match_the_learner_table_sizes_and_order()
    {
        Assert.Equal([1, 2, 3, 4], IrregularVerbTable.Steps.Keys.OrderBy(k => k));
        Assert.Equal(9, IrregularVerbTable.Steps[1].Count);
        Assert.Equal(3, IrregularVerbTable.Steps[2].Count);
        Assert.Equal(29, IrregularVerbTable.Steps[3].Count);
        Assert.Equal(27, IrregularVerbTable.Steps[4].Count);

        // Order within a step keeps the mini-families together, not alphabetical.
        Assert.Equal(["cut", "put", "let"], IrregularVerbTable.Steps[1].Take(3).Select(v => v.V1));
        Assert.Equal(["buy", "bring", "think"], IrregularVerbTable.Steps[3].Take(3).Select(v => v.V1));
    }

    [Fact]
    public void TitleOf_returns_the_four_pattern_titles()
    {
        Assert.Equal("All three forms alike", IrregularVerbTable.TitleOf(1));
        Assert.Equal("All three forms differ", IrregularVerbTable.TitleOf(4));
    }

    [Fact]
    public void Find_looks_up_by_v1_and_returns_null_for_an_unknown_verb()
    {
        var begin = IrregularVerbTable.Find("begin");

        Assert.NotNull(begin);
        Assert.Equal("began", begin!.V2);
        Assert.Equal("begun", begin.V3);
        Assert.Equal("починати", begin.Translation);

        Assert.Null(IrregularVerbTable.Find("dance"));
    }
}
