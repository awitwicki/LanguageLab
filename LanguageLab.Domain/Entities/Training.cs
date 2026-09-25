using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Training;

namespace LanguageLab.Domain.Entities;

public class Training : BaseEntity
{
    public DateTime CreatedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public TrainingMode Mode { get; set; }

    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    /// <summary>null in review mode — it pulls words from every dictionary at once.</summary>
    public Dictionary? Dictionary { get; set; }
    [ForeignKey(nameof(Dictionary))]
    public long? DictionaryId { get; set; }

    public IList<TrainingQuestion> Questions { get; set; } = new List<TrainingQuestion>();
}
