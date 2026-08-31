using LanguageLab.Domain.Training;

namespace LanguageLab.Tests;

public class LeitnerSchedulerTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 2, 3)]   // з боксу 1 у бокс 2 → інтервал боксу 2 = 3 дні
    [InlineData(2, 3, 7)]
    [InlineData(3, 4, 14)]
    [InlineData(4, 5, 30)]
    public void CorrectAnswer_PromotesBoxAndSchedulesByNewBoxInterval(int box, int expectedBox, int expectedDays)
    {
        var outcome = LeitnerScheduler.Grade(box, allCorrect: true, Now);

        Assert.Equal(expectedBox, outcome.Box);
        Assert.Equal(Now.AddDays(expectedDays), outcome.DueAt);
        Assert.False(outcome.IsLearned);
    }

    [Fact]
    public void CorrectAnswer_InTopBox_MarksWordLearnedAndClearsDueDate()
    {
        var outcome = LeitnerScheduler.Grade(LeitnerScheduler.MaxBox, allCorrect: true, Now);

        Assert.Equal(LeitnerScheduler.MaxBox, outcome.Box);
        Assert.True(outcome.IsLearned);
        Assert.Null(outcome.DueAt);
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    public void WrongAnswer_DemotesBoxByOneAndSchedulesTomorrow(int box, int expectedBox)
    {
        var outcome = LeitnerScheduler.Grade(box, allCorrect: false, Now);

        Assert.Equal(expectedBox, outcome.Box);
        Assert.Equal(Now.AddDays(1), outcome.DueAt);
        Assert.False(outcome.IsLearned);
    }

    [Fact]
    public void WrongAnswer_InFirstBox_StaysInFirstBox()
    {
        var outcome = LeitnerScheduler.Grade(1, allCorrect: false, Now);

        Assert.Equal(1, outcome.Box);
        Assert.Equal(Now.AddDays(1), outcome.DueAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void InvalidBox_Throws(int box)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LeitnerScheduler.Grade(box, allCorrect: true, Now));
    }

    [Fact]
    public void IntervalTable_MatchesSpec()
    {
        Assert.Equal(new[] { 1, 3, 7, 14, 30 }, LeitnerScheduler.IntervalDays);
    }
}
