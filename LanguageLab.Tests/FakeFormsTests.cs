using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class FakeFormsTests
{
    private static IReadOnlyList<string> Of(string v1) => FakeForms.Of(IrregularVerbCatalog.Find(v1)!);

    [Theory]
    [InlineData("put", "putted", "putten")]
    [InlineData("cut", "cutted", "cutten")]
    [InlineData("shut", "shutted", "shutten")]
    [InlineData("run", "runned", "runnen")]
    [InlineData("begin", "beginned", "beginnen")]
    [InlineData("forget", "forgetted", "forgetten")]
    [InlineData("go", "goed", "goen")]
    [InlineData("drink", "drinked", null)]
    [InlineData("make", "maked", "maken")]
    [InlineData("fly", "flied", "flien")]
    [InlineData("pay", "payed", "payen")]
    [InlineData("read", "readed", "readen")]
    public void Regularises_by_ed_and_en(string v1, string ed, string? en)
    {
        var fakes = Of(v1);

        Assert.Equal(ed, fakes[0]);

        if (en is not null)
        {
            Assert.Contains(en, fakes);
        }
    }

    [Fact]
    public void Swaps_the_vowel_of_an_ing_verb()
    {
        Assert.Equal(["bringed", "bringen", "brang", "brung"], Of("bring"));
    }

    [Fact]
    public void Never_offers_a_real_form_of_the_verb_as_a_fake()
    {
        foreach (var verb in IrregularVerbCatalog.Verbs)
        {
            var real = verb.V2.Concat(verb.V3).Append(verb.V1).ToHashSet();

            Assert.DoesNotContain(FakeForms.Of(verb), real.Contains);
        }

        // drink → drank / drunk are real, so the vowel swap yields nothing for it.
        Assert.DoesNotContain("drank", Of("drink"));
    }

    [Fact]
    public void Drops_real_english_words_the_rules_would_produce()
    {
        Assert.DoesNotContain("costed", Of("cost"));
        Assert.DoesNotContain("thank", Of("think"));
        Assert.DoesNotContain("bed", Of("be"));
        Assert.DoesNotContain("seed", Of("see"));
        Assert.DoesNotContain("singed", Of("sing"));
        Assert.DoesNotContain("ringed", Of("ring"));
        Assert.DoesNotContain("haven", Of("have"));
        Assert.DoesNotContain("leaven", Of("leave"));
    }

    [Fact]
    public void Every_fake_is_distinct_lowercase_letters()
    {
        foreach (var verb in IrregularVerbCatalog.Verbs)
        {
            var fakes = FakeForms.Of(verb);

            Assert.Equal(fakes.Count, fakes.Distinct().Count());
            Assert.All(fakes, f => Assert.Matches("^[a-z]+$", f));
        }
    }
}
