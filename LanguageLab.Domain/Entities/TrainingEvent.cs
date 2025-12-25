using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

public class TrainingEvent : BaseEntity
{
    public DateTime CreatedAt { get; set; }
    
    public TelegramUser User { get; set; }
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }
    
    public WordPair WordPair { get; set; }
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }
    
    public Training Training { get; set; }
    [ForeignKey(nameof(Training))]
    public long TrainingId { get; set; }
    
    public bool IsCorrect { get; set; }
}
