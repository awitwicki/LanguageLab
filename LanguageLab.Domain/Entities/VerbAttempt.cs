using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Domain.Entities;

/// <summary>Append-only log of every answer, one row per check (a Match pair, each TripleType field).</summary>
public class VerbAttempt : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Verb { get; set; }

    public VerbSession Session { get; set; } = null!;
    [ForeignKey(nameof(Session))]
    public long SessionId { get; set; }

    public VerbTask Task { get; set; } = null!;
    [ForeignKey(nameof(Task))]
    public long TaskId { get; set; }

    public ExerciseType Type { get; set; }

    public FormAsked FormAsked { get; set; }

    public string AnswerGiven { get; set; } = string.Empty;

    public AttemptOutcome Outcome { get; set; }

    public ErrorKind? ErrorKind { get; set; }

    /// <summary>Reported by the client; logged for Phase 2, not used yet.</summary>
    public int? ResponseMs { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
