using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Training;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// An item of a session's pre-generated question queue. Exists before the user answers:
/// IsCorrect == null means "not answered yet".
/// </summary>
public class TrainingQuestion : BaseEntity
{
    public DateTime CreatedAt { get; set; }

    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    public Training Training { get; set; } = null!;
    [ForeignKey(nameof(Training))]
    public long TrainingId { get; set; }

    /// <summary>Position in the queue, 0-based.</summary>
    public int Order { get; set; }

    public QuestionDirection Direction { get; set; }

    /// <summary>WordPair ids in display order. Maps to a Postgres bigint[].</summary>
    public List<long> OptionIds { get; set; } = [];

    public long? PickedWordPairId { get; set; }

    public bool? IsCorrect { get; set; }

    public DateTime? AnsweredAt { get; set; }
}
