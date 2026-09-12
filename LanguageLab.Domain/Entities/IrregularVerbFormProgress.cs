using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>The standing of one form for one user, derived from its progress row rather than stored.</summary>
public enum FormState
{
    Unseen,
    Missed,
    Learning,
    Learned,
}

/// <summary>
/// One user's history with one form of one irregular verb, keyed by the verb's V1 (the
/// stable id from IrregularVerbTable) and the form. Verbs are never their own database
/// row, so these rows are the only place a user's grades live. A form is learned after
/// FormStates.LearnedStreak correct grades in a row; a miss starts the count over.
/// </summary>
public class IrregularVerbFormProgress : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    public VerbForm Form { get; set; }

    /// <summary>Consecutive correct grades; a miss resets this to 0. Drives the derived FormState.</summary>
    public int Streak { get; set; }

    public int Correct { get; set; }

    public int Wrong { get; set; }

    /// <summary>UTC.</summary>
    public DateTime LastAnsweredAt { get; set; }

    public void Apply(bool correct, DateTime nowUtc)
    {
        if (correct)
        {
            Correct++;
            Streak++;
        }
        else
        {
            Wrong++;
            Streak = 0;
        }

        LastAnsweredAt = nowUtc;
    }
}

public static class FormStates
{
    /// <summary>Correct grades in a row that make a form learned.</summary>
    public const int LearnedStreak = 3;

    /// <summary>No row = never graded; otherwise the streak decides.</summary>
    public static FormState Of(IrregularVerbFormProgress? progress) =>
        progress == null ? FormState.Unseen : OfStreak(progress.Streak);

    /// <summary>Streak 0 = missed on the last grade; below the threshold = learning; otherwise learned.</summary>
    public static FormState OfStreak(int streak) =>
        streak == 0 ? FormState.Missed
        : streak < LearnedStreak ? FormState.Learning
        : FormState.Learned;
}
