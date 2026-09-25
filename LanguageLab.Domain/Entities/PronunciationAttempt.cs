using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Domain.Entities;

/// <summary>Append-only log of every graded pronunciation attempt.</summary>
public class PronunciationAttempt : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Word { get; set; }

    public Accent Accent { get; set; }

    public required string Transcript { get; set; }

    public int Score { get; set; }

    public PronunciationOutcome Outcome { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
