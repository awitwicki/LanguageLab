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
    /// UTC. Потрібен колонкам «останні 10» і серверному undo: без нього
    /// історія сортування живе лише в пам'яті вкладки й гине при перезавантаженні.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
