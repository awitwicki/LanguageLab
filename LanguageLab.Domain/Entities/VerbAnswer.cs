using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// Append-only log of every card the learner judged — one row per click. Every metric the
/// trainer shows, and any it grows later, is derivable from this table.
/// </summary>
public class VerbAnswer : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    public PromptForm PromptForm { get; set; }

    /// <summary>The learner's own verdict.</summary>
    public bool Known { get; set; }

    /// <summary>From the card appearing to the click, as the browser measured it; clamped on the way in.</summary>
    public int ResponseMs { get; set; }

    public DrillMode Mode { get; set; }

    /// <summary>The stage the run was started from; null for a free run over the whole catalog.</summary>
    public int? Group { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
