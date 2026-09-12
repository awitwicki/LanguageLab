using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class VerbProgressTests
{
    private static readonly IrregularVerb Go = IrregularVerbTable.Find("go")!;
    private static readonly DateTime T1 = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);

    private static IrregularVerbFormProgress Row(string verb, VerbForm form, int streak, DateTime at) =>
        new() { UserId = 1, Verb = verb, Form = form, Streak = streak, Correct = streak, LastAnsweredAt = at };

    [Fact]
    public void Without_rows_every_form_is_unseen_and_v1_opens()
    {
        var progress = VerbProgress.Of(Go, []);

        Assert.Equal([VerbForm.V1, VerbForm.V2, VerbForm.V3], progress.Forms.Select(f => f.Form));
        Assert.All(progress.Forms, f => Assert.Equal(FormState.Unseen, f.State));
        Assert.False(progress.HasRows);
        Assert.False(progress.HasMiss);
        Assert.False(progress.IsLearned);
        Assert.Equal(0, progress.LearnedForms);
        Assert.Equal(0, progress.StreakSum);
        Assert.Null(progress.LastAnsweredAt);
        Assert.Equal(VerbForm.V1, progress.OpenForm);
    }

    [Fact]
    public void Folds_rows_of_this_verb_only_and_fills_the_missing_forms()
    {
        var rows = new[]
        {
            Row("go", VerbForm.V2, 3, T1),
            Row("go", VerbForm.V3, 0, T2),
            Row("see", VerbForm.V1, 5, T2),
        };

        var progress = VerbProgress.Of(Go, rows);

        Assert.Equal([FormState.Unseen, FormState.Learned, FormState.Missed], progress.Forms.Select(f => f.State));
        Assert.Equal([0, 3, 0], progress.Forms.Select(f => f.Streak));
        Assert.Equal([0, 3, 0], progress.Forms.Select(f => f.Correct));
        Assert.True(progress.HasRows);
        Assert.True(progress.HasMiss);
        Assert.False(progress.IsLearned);
        Assert.Equal(1, progress.LearnedForms);
        Assert.Equal(3, progress.StreakSum);
        Assert.Equal(T2, progress.LastAnsweredAt);
    }

    [Fact]
    public void Is_learned_only_when_all_three_forms_are()
    {
        var twoOfThree = new[] { Row("go", VerbForm.V1, 3, T1), Row("go", VerbForm.V2, 4, T1), Row("go", VerbForm.V3, 2, T1) };
        Assert.False(VerbProgress.Of(Go, twoOfThree).IsLearned);

        var all = new[] { Row("go", VerbForm.V1, 3, T1), Row("go", VerbForm.V2, 4, T1), Row("go", VerbForm.V3, 3, T1) };
        Assert.True(VerbProgress.Of(Go, all).IsLearned);
    }

    [Fact]
    public void The_open_form_is_the_highest_streak_lowest_form_on_a_tie()
    {
        var learnedPast = new[] { Row("go", VerbForm.V2, 3, T1), Row("go", VerbForm.V3, 3, T1) };
        Assert.Equal(VerbForm.V2, VerbProgress.Of(Go, learnedPast).OpenForm);

        var strongestParticiple = new[] { Row("go", VerbForm.V1, 1, T1), Row("go", VerbForm.V3, 2, T1) };
        Assert.Equal(VerbForm.V3, VerbProgress.Of(Go, strongestParticiple).OpenForm);
    }
}
