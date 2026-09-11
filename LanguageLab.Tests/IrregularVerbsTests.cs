using LanguageLab.Application.Seeding;

namespace LanguageLab.Tests;

public class IrregularVerbsTests
{
    [Fact]
    public void Every_verb_is_unique_translated_and_in_a_titled_group()
    {
        var verbs = IrregularVerbs.All;

        Assert.Equal(verbs.Count, verbs.Select(v => v.Word).Distinct(StringComparer.Ordinal).Count());
        Assert.All(verbs, v => Assert.False(string.IsNullOrWhiteSpace(v.Translation), v.Word));
        Assert.All(verbs, v => Assert.InRange(v.Group, 1, IrregularVerbs.GroupTitles.Count));
    }

    [Fact]
    public void Groups_match_the_learner_table()
    {
        var sizes = IrregularVerbs.All
            .GroupBy(v => v.Group)
            .OrderBy(g => g.Key)
            .Select(g => g.Count());

        Assert.Equal([9, 3, 29, 27], sizes);
        Assert.Equal("be – was/were – been", IrregularVerbs.All.Single(v => v.V1 == "be").Word);
    }
}
