using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Pronunciation;

namespace LanguageLab.Domain.Entities;

/// <summary>One user's standing on one catalog word. Created on the word's first attempt.</summary>
public class PronunciationProgress : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public required string Word { get; set; }

    public PronunciationState State { get; set; } = PronunciationState.New;

    /// <summary>Correct attempts in a row; a wrong attempt resets it without demoting State.</summary>
    public int Streak { get; set; }

    /// <summary>UTC.</summary>
    public DateTime LastSeenAt { get; set; }
}
