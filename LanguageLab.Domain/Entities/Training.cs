using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

public class Training : BaseEntity
{
    public DateTime CreatedAt { get; set; }
    
    public TelegramUser User { get; set; }
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }
    
    public Dictionary Dictionary { get; set; }
    [ForeignKey(nameof(Dictionary))]
    public long DictionaryId { get; set; }
    
    public IList<TrainingEvent> Events { get; set; }
}
