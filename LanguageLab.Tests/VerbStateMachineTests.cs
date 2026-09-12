using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Tests;

public class VerbStateMachineTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private static VerbProgress At(VerbState state, int streak = 0, int clean = 0) =>
        new() { UserId = 1, Verb = "go", State = state, Streak = streak, CleanSessions = clean };

    [Theory]
    [InlineData(VerbState.New, 1)]
    [InlineData(VerbState.Learning1, 2)]
    [InlineData(VerbState.Learning2, 3)]
    [InlineData(VerbState.Learning3, 3)]
    [InlineData(VerbState.Learned, 3)]
    [InlineData(VerbState.Forgotten, 3)]
    public void Task_level_follows_the_state(VerbState state, int level)
    {
        Assert.Equal(level, TaskLevels.For(state));
    }

    [Fact]
    public void A_card_moves_a_new_verb_to_learning_1()
    {
        var p = At(VerbState.New);

        VerbStateMachine.ApplyCard(p, Now);

        Assert.Equal(VerbState.Learning1, p.State);
        Assert.Equal(Now, p.LastSeenAt);
    }

    [Fact]
    public void A_card_does_not_touch_a_verb_already_past_new()
    {
        var p = At(VerbState.Learning2, streak: 2);

        VerbStateMachine.ApplyCard(p, Now);

        Assert.Equal(VerbState.Learning2, p.State);
        Assert.Equal(2, p.Streak);
    }

    [Fact]
    public void Three_correct_in_a_row_promote_learning_1_to_2_and_reset_the_streak()
    {
        var p = At(VerbState.Learning1, streak: 2);

        VerbStateMachine.ApplyCorrect(p, Now);

        Assert.Equal(VerbState.Learning2, p.State);
        Assert.Equal(0, p.Streak);
    }

    [Theory]
    [InlineData(VerbState.Learning2)]
    [InlineData(VerbState.Forgotten)]
    public void Three_correct_in_a_row_promote_to_learning_3(VerbState from)
    {
        var p = At(from, streak: 2);

        VerbStateMachine.ApplyCorrect(p, Now);

        Assert.Equal(VerbState.Learning3, p.State);
        Assert.Equal(0, p.Streak);
    }

    [Theory]
    [InlineData(VerbState.Learning3)]
    [InlineData(VerbState.Learned)]
    public void Correct_answers_beyond_learning_3_only_grow_the_streak(VerbState state)
    {
        var p = At(state, streak: 5);

        VerbStateMachine.ApplyCorrect(p, Now);

        Assert.Equal(state, p.State);
        Assert.Equal(6, p.Streak);
    }

    [Theory]
    [InlineData(VerbState.Learned, VerbState.Learning3)]
    [InlineData(VerbState.Learning3, VerbState.Learning2)]
    [InlineData(VerbState.Learning2, VerbState.Learning1)]
    [InlineData(VerbState.Learning1, VerbState.Learning1)]
    [InlineData(VerbState.Forgotten, VerbState.Forgotten)]
    public void A_mistake_steps_down_one_state_and_resets_the_counters(VerbState from, VerbState to)
    {
        var p = At(from, streak: 2, clean: 1);

        VerbStateMachine.ApplyWrong(p, FormAsked.V3, wrongFields: 1, Now);

        Assert.Equal(to, p.State);
        Assert.Equal(0, p.Streak);
        Assert.Equal(0, p.CleanSessions);
        Assert.Equal(0, p.ErrorsV2);
        Assert.Equal(1, p.ErrorsV3);
    }

    [Fact]
    public void A_mistake_on_both_forms_counts_each_wrong_field()
    {
        var p = At(VerbState.Learning2);

        VerbStateMachine.ApplyWrong(p, FormAsked.Both, wrongFields: 2, Now);

        Assert.Equal(1, p.ErrorsV2);
        Assert.Equal(1, p.ErrorsV3);
    }

    [Fact]
    public void A_recognition_mistake_counts_against_neither_form()
    {
        var p = At(VerbState.Learning1);

        VerbStateMachine.ApplyWrong(p, FormAsked.Recognition, wrongFields: 1, Now);

        Assert.Equal(0, p.ErrorsV2 + p.ErrorsV3);
    }

    [Fact]
    public void A_clean_session_counts_and_two_of_them_make_learning_3_learned()
    {
        var p = At(VerbState.Learning3, clean: 1);
        p.ManuallyFlaggedAt = Now;

        VerbStateMachine.ApplySessionEnd(p, clean: true, Now);

        Assert.Equal(VerbState.Learned, p.State);
        Assert.Equal(2, p.CleanSessions);
        Assert.Null(p.ManuallyFlaggedAt);
    }

    [Fact]
    public void One_clean_session_is_not_enough_and_an_unclean_one_resets_the_count()
    {
        var one = At(VerbState.Learning3);
        VerbStateMachine.ApplySessionEnd(one, clean: true, Now);
        Assert.Equal(VerbState.Learning3, one.State);
        Assert.Equal(1, one.CleanSessions);

        var reset = At(VerbState.Learning3, clean: 1);
        VerbStateMachine.ApplySessionEnd(reset, clean: false, Now);
        Assert.Equal(0, reset.CleanSessions);
    }

    [Fact]
    public void A_forgotten_verb_with_two_clean_sessions_is_learned_again()
    {
        var p = At(VerbState.Forgotten, clean: 1);
        p.ManuallyFlaggedAt = Now;

        VerbStateMachine.ApplySessionEnd(p, clean: true, Now);

        Assert.Equal(VerbState.Learned, p.State);
        Assert.Null(p.ManuallyFlaggedAt);
    }

    [Fact]
    public void Forgot_flags_the_verb_from_any_state()
    {
        var p = At(VerbState.Learned, streak: 4, clean: 3);

        VerbStateMachine.ApplyForgot(p, Now);

        Assert.Equal(VerbState.Forgotten, p.State);
        Assert.Equal(0, p.Streak);
        Assert.Equal(0, p.CleanSessions);
        Assert.Equal(Now, p.ManuallyFlaggedAt);
        Assert.Equal(Now, p.NextReviewAt);
    }
}
