using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

public class UnknownWord : BaseEntity
{
    public TelegramUser User { get; set; }
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }
    
    public WordPair WordPair { get; set; }
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    /// <summary>
    /// UTC. Needed by the "last 10" columns and the server-side undo: without it
    /// the sorting history lives only in the tab's memory and dies on reload.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
