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
    public void Families_follow_the_learning_path_with_the_expected_sizes()
    {
        var sizes = IrregularVerbCatalog.Families.Select(f => IrregularVerbCatalog.VerbsOf(f.Key).Count);

        Assert.Equal([9, 3, 5, 5, 2, 3, 2, 4, 8, 5, 6, 4, 3, 6, 3], sizes);
        Assert.Equal([1, 2, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4], IrregularVerbCatalog.Families.Select(f => f.Group));
        Assert.Equal(["same", "back", "ought"], IrregularVerbCatalog.Families.Take(3).Select(f => f.Key));
    }

    [Fact]
    public void Every_verb_belongs_to_a_family_of_its_group_in_path_order()
    {
        var lastIndex = -1;

        foreach (var verb in IrregularVerbCatalog.Verbs)
        {
            var family = IrregularVerbCatalog.FamilyOf(verb);
            Assert.Equal(verb.Group, family.Group);

            var index = IrregularVerbCatalog.FamilyIndex(family.Key);
            Assert.True(index >= lastIndex, $"{verb.V1} is out of family order");
            lastIndex = index;
        }
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
    public void Example_exposes_the_gap_and_plain_variants()
    {
        var example = IrregularVerbCatalog.Find("go")!.ExampleOf(Tense.Past);

        Assert.Equal("went", example.Bracketed);
        Assert.Equal("They ___ home early.", example.WithGap);
        Assert.Equal("They went home early.", example.Plain);
    }

    [Fact]
    public void Alternative_forms_are_split_and_joined()
    {
        var be = IrregularVerbCatalog.Find("be")!;
        var get = IrregularVerbCatalog.Find("get")!;

        Assert.Equal(["was", "were"], be.V2);
        Assert.Equal("be – was / were – been", be.Triplet);
        Assert.Equal(["got", "gotten"], get.V3);
        Assert.Equal(["get", "got", "gotten"], get.AllForms);
        Assert.False(get.SecondAndThirdAlike);
        Assert.True(IrregularVerbCatalog.Find("buy")!.SecondAndThirdAlike);
    }

    [Fact]
    public void Lookups()
    {
        Assert.Null(IrregularVerbCatalog.Find("walk"));
        Assert.Null(IrregularVerbCatalog.FindFamily("nope"));
        Assert.Equal(9, IrregularVerbCatalog.VerbsOfGroup(1).Count);
        Assert.Equal(7, IrregularVerbCatalog.FamiliesOfGroup(3).Count);
        Assert.Equal("All three forms differ", IrregularVerbCatalog.GroupTitle(4));
        Assert.Contains("cat", IrregularVerbCatalog.Find("cut")!.Confusables!);
        Assert.NotNull(IrregularVerbCatalog.Find("read")!.Note);
    }

    [Fact]
    public void Task_payload_round_trips_through_json()
    {
        var payload = new TaskPayload
        {
            Sentence = "They ___ home early.",
            Tense = Tense.Past,
            Options = ["went", "goed", "gone"],
            Correct = "went",
            Pairs = [new MatchPair("go", "went")],
            Form = FormAsked.V2,
        };

        var back = TaskPayload.Deserialize(payload.Serialize());

        Assert.Equal(payload.Sentence, back.Sentence);
        Assert.Equal(Tense.Past, back.Tense);
        Assert.Equal(payload.Options, back.Options);
        Assert.Equal(payload.Pairs, back.Pairs);
        Assert.Equal(FormAsked.V2, back.Form);
        Assert.Null(back.Hint);
    }
}
