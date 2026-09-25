using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// One learner's standing on one irregular verb, keyed by its V1 from the catalog and
/// created on that verb's first answer. <see cref="Mastery"/> is a moving average of answer
/// quality and drives the table's colour; <see cref="Streak"/> counts consecutive "I know"
/// answers and drives nothing but batch progression.
/// </summary>
public class VerbKnowledge : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    /// <summary>0..1.</summary>
    public double Mastery { get; set; }

    /// <summary>Consecutive "I know" answers; any miss resets it.</summary>
    public int Streak { get; set; }

    public int Answers { get; set; }

    public int Knows { get; set; }

    /// <summary>UTC.</summary>
    public DateTime LastAnsweredAt { get; set; }
}
