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
}
