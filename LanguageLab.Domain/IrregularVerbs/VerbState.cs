namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// Where a verb stands for one user. Learning1..3 mirror the three task levels
/// (card, choice, typing); Learned needs two clean level-3 sessions; Forgotten is the
/// learner's own flag (or, in Phase 2, two misses in review) and trains at level 3 with
/// top priority. Mastered is reserved for the Phase 2 review schedule.
/// </summary>
public enum VerbState
{
    New,
    Learning1,
    Learning2,
    Learning3,
    Learned,
    Mastered,
    Forgotten,
}

public static class TaskLevels
{
    public const int Card = 1;
    public const int Recognition = 2;
    public const int Production = 3;

    /// <summary>New verbs get the card; Learning1 practises recognition; everything else produces the forms.</summary>
    public static int For(VerbState state) =>
        state switch
        {
            VerbState.New => Card,
            VerbState.Learning1 => Recognition,
            _ => Production,
        };
}

/// <summary>
/// The transitions of the learner's spec, applied to a progress row. Pure: time comes in,
/// nothing is loaded. Three correct in a row climb one Learning step; a mistake resets
/// the streak and steps down one; two clean level-3 sessions make a verb Learned.
/// </summary>
public static class VerbStateMachine
{
    public const int PromotionStreak = 3;
    public const int CleanSessionsToLearn = 2;

    public static void ApplyCard(Entities.VerbProgress p, DateTime nowUtc)
    {
        if (p.State == VerbState.New)
        {
            p.State = VerbState.Learning1;
        }

        p.LastSeenAt = nowUtc;
    }

    public static void ApplyCorrect(Entities.VerbProgress p, DateTime nowUtc)
    {
        p.Streak++;
        p.LastSeenAt = nowUtc;

        if (p.Streak < PromotionStreak)
        {
            return;
        }

        switch (p.State)
        {
            case VerbState.Learning1:
                p.State = VerbState.Learning2;
                p.Streak = 0;
                break;
            case VerbState.Learning2:
            case VerbState.Forgotten:
                p.State = VerbState.Learning3;
                p.Streak = 0;
                break;
        }
    }

    public static void ApplyWrong(Entities.VerbProgress p, FormAsked form, int wrongFields, DateTime nowUtc)
    {
        p.Streak = 0;
        p.CleanSessions = 0;
        p.LastSeenAt = nowUtc;

        switch (form)
        {
            case FormAsked.V2:
                p.ErrorsV2 += wrongFields;
                break;
            case FormAsked.V3:
                p.ErrorsV3 += wrongFields;
                break;
            case FormAsked.Both:
                // TripleType reports how many fields were wrong; with both wrong each form takes one.
                p.ErrorsV2 += wrongFields >= 2 ? 1 : 0;
                p.ErrorsV3 += wrongFields >= 1 ? 1 : 0;
                break;
        }

        p.State = p.State switch
        {
            VerbState.Learned => VerbState.Learning3,
            VerbState.Learning3 => VerbState.Learning2,
            VerbState.Learning2 => VerbState.Learning1,
            var same => same,
        };
    }

    /// <summary>Clean = the verb had level-3 tasks in the session and none was wrong.</summary>
    public static void ApplySessionEnd(Entities.VerbProgress p, bool clean, DateTime nowUtc)
    {
        p.CleanSessions = clean ? p.CleanSessions + 1 : 0;

        if (p.CleanSessions >= CleanSessionsToLearn && p.State is VerbState.Learning3 or VerbState.Forgotten)
        {
            p.State = VerbState.Learned;
            p.ManuallyFlaggedAt = null;
        }
    }

    public static void ApplyForgot(Entities.VerbProgress p, DateTime nowUtc)
    {
        p.State = VerbState.Forgotten;
        p.Streak = 0;
        p.CleanSessions = 0;
        p.ManuallyFlaggedAt = nowUtc;
        p.NextReviewAt = nowUtc;
        p.LastSeenAt = nowUtc;
    }
}
