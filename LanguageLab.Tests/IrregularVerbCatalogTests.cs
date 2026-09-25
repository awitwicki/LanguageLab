using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class IrregularVerbCatalogTests
{
    [Fact]
    public void Has_68_verbs_with_unique_keys_and_translations()
    {
        Assert.Equal(68, IrregularVerbCatalog.Verbs.Count);
        Assert.Equal(68, IrregularVerbCatalog.Verbs.Select(v => v.V1).Distinct().Count());
        Assert.All(IrregularVerbCatalog.Verbs, v => Assert.False(string.IsNullOrWhiteSpace(v.Translation)));
    }

    [Fact]
    public void Four_stages_hold_the_expected_counts_in_order()
    {
        Assert.Equal(4, IrregularVerbCatalog.GroupCount);
        Assert.Equal([9, 3, 29, 27], Enumerable.Range(1, 4).Select(g => IrregularVerbCatalog.VerbsOfGroup(g).Count));
        Assert.Equal([1, 2, 3, 4], IrregularVerbCatalog.Verbs.Select(v => v.Group).Distinct());
        Assert.Equal("All three forms differ", IrregularVerbCatalog.GroupTitle(4));
    }

    [Fact]
    public void Verbs_are_grouped_together_in_stage_order()
    {
        var groups = IrregularVerbCatalog.Verbs.Select(v => v.Group).ToList();
        Assert.Equal(groups.OrderBy(g => g), groups);
    }

    [Fact]
    public void Cumulative_scope_adds_up_the_earlier_stages()
    {
        Assert.Equal(9, IrregularVerbCatalog.VerbsUpToGroup(1).Count);
        Assert.Equal(12, IrregularVerbCatalog.VerbsUpToGroup(2).Count);
        Assert.Equal(68, IrregularVerbCatalog.VerbsUpToGroup(4).Count);
    }

    /// <summary>A bare form has to identify one verb: the drill shows nothing else.</summary>
    [Fact]
    public void Every_form_in_the_catalog_belongs_to_exactly_one_verb()
    {
        var owners = IrregularVerbCatalog.Verbs
            .SelectMany(v => new[] { v.V1 }.Concat(v.V2).Concat(v.V3).Distinct(StringComparer.Ordinal).Select(f => (Form: f, v.V1)))
            .GroupBy(x => x.Form, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.V1).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(owners);
    }

    [Fact]
    public void Every_verb_has_one_example_per_tense_with_the_matching_form_in_brackets()
    {
        foreach (var verb in IrregularVerbCatalog.Verbs)
        {
            Assert.Equal([Tense.Present, Tense.Past, Tense.Perfect], verb.Examples.Select(e => e.Tense));

            foreach (var example in verb.Examples)
            {
                Assert.Equal(1, example.Text.Count(c => c == '['));
                Assert.Equal(1, example.Text.Count(c => c == ']'));

                IReadOnlyList<string> expected = example.Tense switch
                {
                    Tense.Present => [verb.V1],
                    Tense.Past => verb.V2,
                    _ => verb.V3,
                };

                Assert.Contains(example.Bracketed, expected);
            }
        }
    }

    [Fact]
    public void Alternative_forms_are_split_and_joined()
    {
        var be = IrregularVerbCatalog.Find("be")!;

        Assert.Equal(["was", "were"], be.V2);
        Assert.Equal("be – was / were – been", be.Triplet);
        Assert.Equal(["got", "gotten"], IrregularVerbCatalog.Find("get")!.V3);
        Assert.Equal("went", IrregularVerbCatalog.Find("go")!.ExampleOf(Tense.Past).Bracketed);
    }

    [Fact]
    public void Lookups()
    {
        Assert.Null(IrregularVerbCatalog.Find("walk"));
        Assert.NotNull(IrregularVerbCatalog.Find("read")!.Note);
    }
}
