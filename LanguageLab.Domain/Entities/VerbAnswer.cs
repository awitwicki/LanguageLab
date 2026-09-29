using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// Append-only log of every card the learner answered — one row per pick. Every metric the
/// trainer shows, and any it grows later, is derivable from this table.
/// </summary>
public class VerbAnswer : BaseEntity
{
    public const int ChosenMaxLength = 32;

    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    public PromptForm PromptForm { get; set; }

    /// <summary>Whether the pick was a right form for the blank — judged on the server from <see cref="Chosen"/>.</summary>
    public bool Known { get; set; }

    /// <summary>The option the learner picked, right or wrong — the fakes that fool people are readable from here.</summary>
    [MaxLength(ChosenMaxLength)]
    public string Chosen { get; set; } = "";

    /// <summary>From the card appearing to the click, as the browser measured it; clamped on the way in.</summary>
    public int ResponseMs { get; set; }

    public DrillMode Mode { get; set; }

    /// <summary>The stage the run was started from; null for a free run over the whole catalog.</summary>
    public int? Group { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
