using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class IrregularVerbFormProgressTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_correct_grade_increments_the_streak_and_the_correct_count()
    {
        var progress = new IrregularVerbFormProgress { UserId = 1, Verb = "go", Form = VerbForm.V2, Streak = 1, Correct = 1 };

        progress.Apply(correct: true, Now);

        Assert.Equal(2, progress.Streak);
        Assert.Equal(2, progress.Correct);
        Assert.Equal(0, progress.Wrong);
        Assert.Equal(Now, progress.LastAnsweredAt);
    }

    [Fact]
    public void A_miss_resets_the_streak_and_counts_a_wrong()
    {
        var progress = new IrregularVerbFormProgress { UserId = 1, Verb = "go", Form = VerbForm.V2, Streak = 2, Correct = 2 };

        progress.Apply(correct: false, Now);

        Assert.Equal(0, progress.Streak);
        Assert.Equal(2, progress.Correct);
        Assert.Equal(1, progress.Wrong);
        Assert.Equal(Now, progress.LastAnsweredAt);
    }

    [Fact]
    public void State_is_unseen_without_a_row()
    {
        Assert.Equal(FormState.Unseen, FormStates.Of(null));
    }

    [Theory]
    [InlineData(0, FormState.Missed)]
    [InlineData(1, FormState.Learning)]
    [InlineData(2, FormState.Learning)]
    [InlineData(3, FormState.Learned)]
    [InlineData(7, FormState.Learned)]
    public void State_follows_the_streak(int streak, FormState expected)
    {
        var progress = new IrregularVerbFormProgress { UserId = 1, Verb = "go", Form = VerbForm.V3, Streak = streak };

        Assert.Equal(expected, FormStates.Of(progress));
        Assert.Equal(expected, FormStates.OfStreak(streak));
    }
}
