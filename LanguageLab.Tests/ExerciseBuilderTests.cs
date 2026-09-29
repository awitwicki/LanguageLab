using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class ExerciseBuilderTests
{
    private static IrregularVerb Verb(string v1) => IrregularVerbCatalog.Find(v1)!;

    [Fact]
    public void Every_template_has_exactly_one_blank()
    {
        foreach (var tense in Enum.GetValues<Tense>())
        {
            Assert.InRange(SentenceTemplates.Of(tense).Count, 8, 14);
            Assert.All(SentenceTemplates.Of(tense), t =>
                Assert.Equal(2, t.Split(SentenceTemplates.Blank).Length));
        }
    }

    [Fact]
    public void Present_templates_never_use_a_third_person_singular_subject()
    {
        Assert.All(SentenceTemplates.Of(Tense.Present), t =>
            Assert.DoesNotMatch(@"\b(he|she|it|He|She|It)\b", t));
    }

    [Fact]
    public void Includes_the_verbs_own_sentence_with_its_bracketed_form_as_the_answer()
    {
        var exercises = ExerciseBuilder.For(Verb("drink"), PromptForm.V3, new Random(1));

        Assert.Contains(exercises, e => e.Before == "She has " && e.After == " all the juice." && e.Answer == "drunk");
        Assert.All(exercises, e => Assert.Equal(PromptForm.V3, e.Form));
    }

    [Fact]
    public void Adds_two_distinct_templates_alongside_the_own_sentence()
    {
        var exercises = ExerciseBuilder.For(Verb("go"), PromptForm.V2, new Random(1));

        Assert.Equal(1 + ExerciseBuilder.TemplatesPerForm, exercises.Count);
        Assert.All(exercises, e => Assert.Equal("went", e.Answer));
        Assert.Equal(exercises.Count, exercises.Select(e => e.Before + e.After).Distinct().Count());

        var templateCount = exercises.Count(e =>
            SentenceTemplates.Of(Tense.Past).Contains(e.Before + SentenceTemplates.Blank + e.After));

        // Exactly the templates match a template sentence; the one left over is the own one.
        Assert.Equal(ExerciseBuilder.TemplatesPerForm, templateCount);
    }

    /// <summary>
    /// Review (final pass): the own sentence used to be built first and returned first every
    /// time, so a learner who always answered right within a form's first three showings never
    /// met a template — the very thing the templates exist to prevent. The order is shuffled
    /// with the rest now, not just the options within one exercise.
    /// </summary>
    [Fact]
    public void The_own_sentence_does_not_always_lead()
    {
        var leadsWithOwnSentence = Enumerable.Range(0, 20)
            .Select(seed => ExerciseBuilder.For(Verb("go"), PromptForm.V2, new Random(seed)))
            .Count(exercises => exercises[0].Before == "They ");

        Assert.InRange(leadsWithOwnSentence, 1, 18);
    }

    [Theory]
    [InlineData("be")]
    [InlineData("cost")]
    [InlineData("mean")]
    public void A_verb_marked_own_sentences_only_gets_no_templates(string v1)
    {
        foreach (var form in Enum.GetValues<PromptForm>())
        {
            Assert.Single(ExerciseBuilder.For(Verb(v1), form, new Random(1)));
        }
    }

    [Fact]
    public void Group_one_offers_the_form_and_its_fakes()
    {
        var exercise = ExerciseBuilder.For(Verb("put"), PromptForm.V2, new Random(1))[0];

        Assert.Equal(["put", "putted", "putten"], exercise.Options.Order());
        Assert.Equal("put", exercise.Answer);
    }

    [Fact]
    public void Group_four_offers_the_other_real_forms_before_fakes()
    {
        var exercise = ExerciseBuilder.For(Verb("drink"), PromptForm.V2, new Random(1))[0];

        Assert.Equal(["drank", "drink", "drinked", "drunk"], exercise.Options.Order());
    }

    [Fact]
    public void Shows_only_one_of_a_forms_alternatives()
    {
        // "We [were] at home yesterday." — "was" would be a second right answer.
        var were = ExerciseBuilder.For(Verb("be"), PromptForm.V2, new Random(1))[0];

        Assert.Equal("were", were.Answer);
        Assert.DoesNotContain("was", were.Options);

        foreach (var exercise in ExerciseBuilder.For(Verb("get"), PromptForm.V3, new Random(1)))
        {
            Assert.Single(exercise.Options, o => o is "got" or "gotten");
        }
    }

    /// <summary>Review focus 2: the browser's verdict and the server's must never disagree.</summary>
    [Fact]
    public void Every_verb_and_form_builds_exercises_with_exactly_one_right_option()
    {
        foreach (var verb in IrregularVerbCatalog.Verbs)
        {
            foreach (var form in Enum.GetValues<PromptForm>())
            {
                var exercises = ExerciseBuilder.For(verb, form, new Random(7));

                Assert.NotEmpty(exercises);

                foreach (var e in exercises)
                {
                    Assert.InRange(e.Options.Count, 2, ExerciseBuilder.MaxOptions);
                    Assert.Equal(e.Options.Count, e.Options.Distinct().Count());
                    Assert.Contains(e.Answer, e.Options);
                    Assert.Single(e.Options, o => verb.FormsOf(form).Contains(o, StringComparer.OrdinalIgnoreCase));
                    Assert.DoesNotContain('[', e.Before + e.After);
                    Assert.DoesNotContain(SentenceTemplates.Blank, e.Before + e.After);
                }
            }
        }
    }

    [Fact]
    public void All_lists_every_form()
    {
        var all = ExerciseBuilder.All(Verb("go"), new Random(1));

        Assert.Equal([PromptForm.V1, PromptForm.V2, PromptForm.V3], all.Select(e => e.Form).Distinct());
        Assert.Equal(9, all.Count);
    }
}
