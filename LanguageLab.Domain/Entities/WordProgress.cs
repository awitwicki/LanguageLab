using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>The Leitner state of a user × word pair. The row appears when the word first enters a batch.</summary>
public class WordProgress : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    /// <summary>1..5, see LeitnerScheduler.</summary>
    public int Box { get; set; } = 1;

    /// <summary>UTC. null means the word is learned and never enters the review queue again.</summary>
    public DateTime? DueAt { get; set; }

    public bool IsLearned { get; set; }

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    public DateTime LastSeenAt { get; set; }
}
