using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Tests;

public class PronunciationStateMachineTests
{
    [Fact]
    public void A_wrong_attempt_on_a_new_word_moves_it_to_learning_with_zero_streak()
    {
        var next = PronunciationStateMachine.Apply(new ProgressState(PronunciationState.New, 0), correct: false);

        Assert.Equal(PronunciationState.Learning, next.State);
        Assert.Equal(0, next.Streak);
    }

    [Fact]
    public void A_correct_attempt_on_a_new_word_moves_it_to_learning_with_streak_one()
    {
        var next = PronunciationStateMachine.Apply(new ProgressState(PronunciationState.New, 0), correct: true);

        Assert.Equal(PronunciationState.Learning, next.State);
        Assert.Equal(1, next.Streak);
    }

    [Fact]
    public void Three_correct_attempts_in_a_row_reach_mastered()
    {
        var state = new ProgressState(PronunciationState.New, 0);
        state = PronunciationStateMachine.Apply(state, correct: true);
        state = PronunciationStateMachine.Apply(state, correct: true);
        state = PronunciationStateMachine.Apply(state, correct: true);

        Assert.Equal(PronunciationState.Mastered, state.State);
        Assert.Equal(3, state.Streak);
    }

    [Fact]
    public void A_wrong_attempt_resets_the_streak_without_demoting_the_state()
    {
        var learning = new ProgressState(PronunciationState.Learning, 2);

        var next = PronunciationStateMachine.Apply(learning, correct: false);

        Assert.Equal(PronunciationState.Learning, next.State);
        Assert.Equal(0, next.Streak);
    }

    [Fact]
    public void A_wrong_attempt_on_a_mastered_word_keeps_it_mastered_but_resets_streak()
    {
        var mastered = new ProgressState(PronunciationState.Mastered, 5);

        var next = PronunciationStateMachine.Apply(mastered, correct: false);

        Assert.Equal(PronunciationState.Mastered, next.State);
        Assert.Equal(0, next.Streak);
    }
}
