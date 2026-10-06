using LanguageLab.Domain.Grammar;

namespace LanguageLab.Tests;

public class GrammarCatalogTests
{
    private static IEnumerable<GrammarExercise> Exercises => GrammarCatalog.Topics.SelectMany(t => t.Exercises);

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [Fact]
    public void Written_topics_follow_syllabus_order_with_five_exercises_each()
    {
        var keys = GrammarCatalog.Topics.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            GrammarSyllabus.Entries.Select(e => e.Key).Where(keys.Contains),
            GrammarCatalog.Topics.Select(t => t.Key));
        Assert.All(GrammarCatalog.Topics, t => Assert.Equal(5, t.Exercises.Count));
    }

    [Fact]
    public void Topic_keys_are_unique()
    {
        var keys = GrammarCatalog.Topics.Select(t => t.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_topic_has_its_labels_an_explanation_and_examples()
    {
        Assert.All(GrammarCatalog.Topics, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Section));
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.NotEmpty(t.Explanation);
            Assert.NotEmpty(t.Examples);
        });
    }

    [Fact]
    public void Every_sentence_has_exactly_one_blank()
    {
        Assert.All(Exercises, e => Assert.Equal(1, Count(e.Sentence, GrammarExercise.Blank)));
    }

    /// <summary>Exactly one option is ever right: the browser judges a pick by equality.</summary>
    [Fact]
    public void Options_are_two_to_four_distinct_and_hold_the_answer()
    {
        Assert.All(Exercises, e =>
        {
            Assert.InRange(e.Options.Count, 2, 4);
            Assert.Equal(e.Options.Count, e.Options.Distinct(StringComparer.Ordinal).Count());
            Assert.Single(e.Options, o => o == e.Answer);
        });
    }

    [Fact]
    public void Every_exercise_explains_its_answer()
    {
        Assert.All(Exercises, e => Assert.False(string.IsNullOrWhiteSpace(e.Why)));
    }

    /// <summary>Review focus 5: the browser turns [brackets] into bold, so a stray one would show.</summary>
    [Fact]
    public void Every_example_marks_exactly_one_form()
    {
        Assert.All(GrammarCatalog.Topics.SelectMany(t => t.Examples), example =>
        {
            Assert.Equal(1, Count(example, "["));
            Assert.Equal(1, Count(example, "]"));
            Assert.True(example.IndexOf('[') + 1 < example.IndexOf(']'), example);
        });
    }

    [Fact]
    public void Brackets_in_explanations_open_and_close_in_pairs()
    {
        Assert.All(GrammarCatalog.Topics.SelectMany(t => t.Explanation), paragraph =>
        {
            var open = false;

            foreach (var c in paragraph)
            {
                if (c == '[')
                {
                    Assert.False(open, paragraph);
                    open = true;
                }
                else if (c == ']')
                {
                    Assert.True(open, paragraph);
                    open = false;
                }
            }

            Assert.False(open, paragraph);
        });
    }

    /// <summary>Review focus 3: a blank opening the sentence takes the capitalised form.</summary>
    [Fact]
    public void A_blank_opening_a_question_offers_capitalised_forms()
    {
        var question = Exercises.Single(e => e.Sentence.StartsWith(GrammarExercise.Blank, StringComparison.Ordinal));

        Assert.Equal("Is", question.Answer);
        Assert.All(question.Options, o => Assert.True(char.IsUpper(o[0]), o));
    }
}
