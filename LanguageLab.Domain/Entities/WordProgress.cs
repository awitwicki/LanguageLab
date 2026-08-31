using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>Стан Leitner для пари юзер × слово. Рядок з'являється, коли слово вперше потрапило в батч.</summary>
public class WordProgress : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    /// <summary>1..5, див. LeitnerScheduler.</summary>
    public int Box { get; set; } = 1;

    /// <summary>UTC. null означає, що слово вивчене й у чергу повторень більше не потрапляє.</summary>
    public DateTime? DueAt { get; set; }

    public bool IsLearned { get; set; }

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    public DateTime LastSeenAt { get; set; }
}
