using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// One user's standing on one irregular verb, keyed by its V1 from the catalog. Created
/// on the first task answered. Ease, IntervalDays and NextReviewAt belong to the Phase 2
/// review schedule and are stored but not read yet.
/// </summary>
public class VerbProgress : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    public VerbState State { get; set; } = VerbState.New;

    /// <summary>Correct tasks in a row at the current level; a wrong task resets it.</summary>
    public int Streak { get; set; }

    /// <summary>Finished sessions in a row with level-3 tasks on this verb and no mistake.</summary>
    public int CleanSessions { get; set; }

    public int ErrorsV2 { get; set; }

    public int ErrorsV3 { get; set; }

    public double Ease { get; set; } = 2.5;

    public int IntervalDays { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? NextReviewAt { get; set; }

    /// <summary>UTC. Set by the Forgot button; cleared after two clean sessions.</summary>
    public DateTime? ManuallyFlaggedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime LastSeenAt { get; set; }
}
