namespace LanguageLab.Domain.Pronunciation;

public enum PronunciationState
{
    New,
    Learning,
    Mastered,
}

public readonly record struct ProgressState(PronunciationState State, int Streak);

/// <summary>
/// Pure per-word progress rule: any attempt moves a New word to Learning; three correct
/// attempts in a row reach Mastered; a wrong attempt resets the streak but never demotes
/// the state (there's only one exercise type here, unlike the verb trainer's levels).
/// </summary>
public static class PronunciationStateMachine
{
    public const int MasteryStreak = 3;

    public static ProgressState Apply(ProgressState current, bool correct)
    {
        var state = current.State == PronunciationState.New ? PronunciationState.Learning : current.State;

        if (!correct)
        {
            return new ProgressState(state, 0);
        }

        var streak = current.Streak + 1;
        if (streak >= MasteryStreak)
        {
            state = PronunciationState.Mastered;
        }

        return new ProgressState(state, streak);
    }
}
