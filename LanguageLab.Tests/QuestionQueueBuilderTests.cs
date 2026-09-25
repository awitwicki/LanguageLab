using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;

namespace LanguageLab.Tests;

public class QuestionQueueBuilderTests
{
    private static WordPair W(long id, string word, string translation) =>
        new() { Id = id, Word = word, Translation = translation };

    private static List<WordPair> Pool(int count, int startId = 100) =>
        Enumerable.Range(0, count)
            .Select(i => W(startId + i, $"word{i}", $"переклад{i}"))
            .ToList();

    private static List<WordPair> FiveTargets() =>
    [
        W(1, "abide", "дотримуватися"),
        W(2, "abdomen", "черевна порожнина"),
        W(3, "ablate", "абляція"),
        W(4, "able", "здатний"),
        W(5, "aback", "зненацька")
    ];

    [Fact]
    public void BatchOfFive_ProducesTenQuestions_EachWordExactlyTwice()
    {
        var targets = FiveTargets();

        var queue = QuestionQueueBuilder.Build(targets, repeats: 2, Pool(20), DirectionPolicy.EnToUa, new Random(1));

        Assert.Equal(10, queue.Count);
        foreach (var target in targets)
        {
            Assert.Equal(2, queue.Count(q => q.WordPairId == target.Id));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(9999)]
    public void SameWordNeverAppearsTwiceInARow(int seed)
    {
        var queue = QuestionQueueBuilder.Build(FiveTargets(), repeats: 2, Pool(20), DirectionPolicy.EnToUa, new Random(seed));

        for (var i = 1; i < queue.Count; i++)
        {
            Assert.NotEqual(queue[i - 1].WordPairId, queue[i].WordPairId);
        }
    }

    [Fact]
    public void EveryQuestionHasSixDistinctOptionsIncludingTheCorrectOne()
    {
        var queue = QuestionQueueBuilder.Build(FiveTargets(), repeats: 2, Pool(20), DirectionPolicy.EnToUa, new Random(3));

        foreach (var question in queue)
        {
            Assert.Equal(QuestionQueueBuilder.OptionsPerQuestion, question.OptionIds.Count);
            Assert.Equal(question.OptionIds.Count, question.OptionIds.Distinct().Count());
            Assert.Contains(question.WordPairId, question.OptionIds);
        }
    }

    [Fact]
    public void DistractorWithSameTranslationAsTarget_IsNeverOffered()
    {
        var target = W(1, "abide", "дотримуватися");
        var twin = W(2, "comply", "дотримуватися");
        var pool = new List<WordPair> { twin, W(3, "cat", "кіт"), W(4, "dog", "пес") };

        var queue = QuestionQueueBuilder.Build([target], repeats: 1, pool, DirectionPolicy.EnToUa, new Random(5));

        Assert.DoesNotContain(twin.Id, queue.Single().OptionIds);
    }

    [Fact]
    public void DistractorWithEmptyTranslation_IsNeverOffered()
    {
        var target = W(1, "abide", "дотримуватися");
        var orphan = W(2, "aback", "");
        var pool = new List<WordPair> { orphan, W(3, "cat", "кіт") };

        var queue = QuestionQueueBuilder.Build([target], repeats: 1, pool, DirectionPolicy.EnToUa, new Random(5));

        Assert.DoesNotContain(orphan.Id, queue.Single().OptionIds);
    }

    [Fact]
    public void PoorDistractorPool_StillProducesUsableQuestion()
    {
        var target = W(1, "abide", "дотримуватися");
        var pool = new List<WordPair> { W(2, "cat", "кіт") };

        var queue = QuestionQueueBuilder.Build([target], repeats: 1, pool, DirectionPolicy.EnToUa, new Random(5));

        var options = queue.Single().OptionIds;
        Assert.Equal(2, options.Count);
        Assert.Contains(1L, options);
        Assert.Contains(2L, options);
    }

    [Fact]
    public void NoValidDistractorAtAll_Throws()
    {
        var target = W(1, "abide", "дотримуватися");
        var pool = new List<WordPair> { W(2, "comply", "дотримуватися") };

        Assert.Throws<InvalidOperationException>(() =>
            QuestionQueueBuilder.Build([target], repeats: 1, pool, DirectionPolicy.EnToUa, new Random(5)));
    }

    [Fact]
    public void EnToUaPolicy_ProducesOnlyEnToUaQuestions()
    {
        var queue = QuestionQueueBuilder.Build(FiveTargets(), repeats: 2, Pool(20), DirectionPolicy.EnToUa, new Random(11));

        Assert.All(queue, q => Assert.Equal(QuestionDirection.EnToUa, q.Direction));
    }

    [Fact]
    public void RandomPolicy_ProducesBothDirections()
    {
        var queue = QuestionQueueBuilder.Build(Pool(30, startId: 1), repeats: 1, Pool(30, startId: 1), DirectionPolicy.Random, new Random(13));

        Assert.Contains(queue, q => q.Direction == QuestionDirection.EnToUa);
        Assert.Contains(queue, q => q.Direction == QuestionDirection.UaToEn);
    }

    [Fact]
    public void SameSeed_ProducesIdenticalQueue()
    {
        var first = QuestionQueueBuilder.Build(FiveTargets(), repeats: 2, Pool(20), DirectionPolicy.Random, new Random(77));
        var second = QuestionQueueBuilder.Build(FiveTargets(), repeats: 2, Pool(20), DirectionPolicy.Random, new Random(77));

        Assert.Equal(
            first.Select(q => (q.WordPairId, q.Direction, string.Join(",", q.OptionIds))),
            second.Select(q => (q.WordPairId, q.Direction, string.Join(",", q.OptionIds))));
    }

    [Fact]
    public void SingleTargetWithTwoRepeats_IsAllowedToRepeatBackToBack()
    {
        var queue = QuestionQueueBuilder.Build([W(1, "abide", "дотримуватися")], repeats: 2, Pool(10), DirectionPolicy.EnToUa, new Random(2));

        Assert.Equal(2, queue.Count);
        Assert.All(queue, q => Assert.Equal(1, q.WordPairId));
    }

    [Fact]
    public void NoTargets_ProducesEmptyQueue()
    {
        var queue = QuestionQueueBuilder.Build([], repeats: 2, Pool(10), DirectionPolicy.EnToUa, new Random(1));

        Assert.Empty(queue);
    }

    [Fact]
    public void RepeatsBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuestionQueueBuilder.Build(FiveTargets(), repeats: 0, Pool(10), DirectionPolicy.EnToUa, new Random(1)));
    }
}
