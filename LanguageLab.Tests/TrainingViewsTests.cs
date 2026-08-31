using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.TgBot.Services;

namespace LanguageLab.Tests;

public class TrainingViewsTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static WordPair Word(long id, string word, string translation) =>
        new() { Id = id, Word = word, Translation = translation };

    private static TrainingQuestion Question(QuestionDirection direction, WordPair word, params long[] optionIds) =>
        new()
        {
            Id = 42,
            WordPairId = word.Id,
            WordPair = word,
            Direction = direction,
            OptionIds = optionIds.ToList()
        };

    [Fact]
    public void QuestionText_ShowsEnglishWordWhenDirectionIsEnToUa()
    {
        var text = TrainingViews.QuestionText(3, 10, "✅ Правильно",
            Question(QuestionDirection.EnToUa, Word(1, "abide", "дотримуватися")));

        Assert.Contains("Питання 3/10", text);
        Assert.Contains("✅ Правильно", text);
        Assert.Contains("<b>abide</b>", text);
        Assert.DoesNotContain("дотримуватися", text);
    }

    [Fact]
    public void QuestionText_ShowsTranslationWhenDirectionIsUaToEn()
    {
        var text = TrainingViews.QuestionText(1, 20, null,
            Question(QuestionDirection.UaToEn, Word(1, "abide", "дотримуватися")));

        Assert.Contains("<b>дотримуватися</b>", text);
        Assert.DoesNotContain("abide", text);
    }

    [Fact]
    public void QuestionText_EscapesHtmlSpecialCharacters()
    {
        var text = TrainingViews.QuestionText(1, 10, null,
            Question(QuestionDirection.EnToUa, Word(1, "a<b>&c", "щось")));

        Assert.Contains("a&lt;b&gt;&amp;c", text);
    }

    [Fact]
    public void OptionLabel_FollowsQuestionDirection()
    {
        var option = Word(1, "abide", "дотримуватися");

        Assert.Equal("дотримуватися", TrainingViews.OptionLabel(option, QuestionDirection.EnToUa));
        Assert.Equal("abide", TrainingViews.OptionLabel(option, QuestionDirection.UaToEn));
    }

    [Fact]
    public void QuestionKeyboard_LaysOutSixOptionsInThreeRowsPlusActionRow()
    {
        var target = Word(1, "abide", "дотримуватися");
        var options = Enumerable.Range(1, 6).Select(i => Word(i, $"w{i}", $"п{i}")).ToList();

        var keyboard = TrainingViews.QuestionKeyboard(
            Question(QuestionDirection.EnToUa, target, 1, 2, 3, 4, 5, 6), options, canDelete: true);

        var rows = keyboard.InlineKeyboard.Select(r => r.ToList()).ToList();

        Assert.Equal(4, rows.Count);
        Assert.All(rows.Take(3), row => Assert.Equal(2, row.Count));
        Assert.Equal(2, rows[3].Count);
        Assert.Equal("tq_42_1", rows[0][0].CallbackData);
        Assert.Equal("tknow_42", rows[3][0].CallbackData);
        Assert.Equal("tdel_42", rows[3][1].CallbackData);
    }

    [Fact]
    public void QuestionKeyboard_HidesDeleteButtonForNonModerators()
    {
        var target = Word(1, "abide", "дотримуватися");
        var options = Enumerable.Range(1, 6).Select(i => Word(i, $"w{i}", $"п{i}")).ToList();

        var keyboard = TrainingViews.QuestionKeyboard(
            Question(QuestionDirection.EnToUa, target, 1, 2, 3, 4, 5, 6), options, canDelete: false);

        var actionRow = keyboard.InlineKeyboard.Last().ToList();

        Assert.Single(actionRow);
        Assert.Equal("tknow_42", actionRow[0].CallbackData);
    }

    [Fact]
    public void SummaryText_ShowsScorePercentageAndPerWordOutcome()
    {
        var summary = new TrainingSummary(9, 10, 0.9, true,
        [
            new WordResult("abide", "дотримуватися", 2, 2, 2, Now.AddDays(3), false),
            new WordResult("aback", "зненацька", 1, 2, 1, Now.AddDays(1), false),
            new WordResult("able", "здатний", 2, 2, 5, null, true)
        ]);

        var text = TrainingViews.SummaryText(summary, Now);

        Assert.Contains("9/10", text);
        Assert.Contains("90%", text);
        Assert.Contains("✅ abide", text);
        Assert.Contains("box 2", text);
        Assert.Contains("02.09", text);
        Assert.Contains("❌ aback", text);
        Assert.Contains("(1/2)", text);
        Assert.Contains("завтра", text);
        Assert.Contains("вивчено", text);
    }

    [Fact]
    public void SummaryKeyboard_OffersNextBatchWhenPassed()
    {
        var summary = new TrainingSummary(9, 10, 0.9, true, []);

        var data = TrainingViews.SummaryKeyboard(summary, trainingId: 7, dictionaryId: 1, dueCount: 4)
            .InlineKeyboard.SelectMany(r => r).Select(b => b.CallbackData).ToList();

        Assert.Contains("tnew_1", data);
        Assert.DoesNotContain("tretry_7", data);
        Assert.Contains("treview", data);
        Assert.Contains("tstop", data);
    }

    [Fact]
    public void SummaryKeyboard_OffersRetryWhenBelowThreshold()
    {
        var summary = new TrainingSummary(6, 10, 0.6, false, []);

        var data = TrainingViews.SummaryKeyboard(summary, trainingId: 7, dictionaryId: 1, dueCount: 0)
            .InlineKeyboard.SelectMany(r => r).Select(b => b.CallbackData).ToList();

        Assert.Contains("tretry_7", data);
        Assert.DoesNotContain("tnew_1", data);
        Assert.DoesNotContain("treview", data);
    }

    [Fact]
    public void CardsText_ListsEveryWordWithItsTranslation()
    {
        var text = TrainingViews.CardsText([Word(1, "abide", "дотримуватися"), Word(2, "aback", "зненацька")]);

        Assert.Contains("<b>abide</b> — дотримуватися", text);
        Assert.Contains("<b>aback</b> — зненацька", text);
    }

    [Fact]
    public void StatsText_RendersEveryBoxLearnedKnownDueAndAccuracy()
    {
        var stats = new TrainingStats(
            BoxCounts: [2, 1, 1, 0, 1],
            Learned: 3,
            Known: 4,
            Due: 5,
            Correct: 9,
            Wrong: 1);

        var text = TrainingViews.StatsText(stats);

        Assert.Contains("box 1: 2", text);
        Assert.Contains("box 2: 1", text);
        Assert.Contains("box 3: 1", text);
        Assert.Contains("box 4: 0", text);
        Assert.Contains("box 5: 1", text);
        Assert.Contains("Вивчено: 3", text);
        Assert.Contains("Знав до навчання: 4", text);
        Assert.Contains("На сьогодні: 5", text);
        Assert.Contains("Правильних відповідей: 90% (9 з 10)", text);
    }

    [Fact]
    public void StatsText_RendersZeroPercentWhenNothingWasAnswered()
    {
        var stats = new TrainingStats(
            BoxCounts: [0, 0, 0, 0, 0],
            Learned: 0,
            Known: 0,
            Due: 0,
            Correct: 0,
            Wrong: 0);

        var text = TrainingViews.StatsText(stats);

        Assert.Contains("Правильних відповідей: 0% (0 з 0)", text);
        Assert.DoesNotContain("NaN", text);
    }
}
