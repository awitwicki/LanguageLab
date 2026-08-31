using System.ComponentModel.DataAnnotations.Schema;
using LanguageLab.Domain.Training;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// Елемент наперед згенерованої черги питань сесії. Існує до того, як юзер відповів:
/// IsCorrect == null означає «ще не відповідав».
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

    /// <summary>Позиція в черзі, 0-based.</summary>
    public int Order { get; set; }

    public QuestionDirection Direction { get; set; }

    /// <summary>Id WordPair у порядку показу. Мапиться на bigint[] Postgres.</summary>
    public List<long> OptionIds { get; set; } = [];

    public long? PickedWordPairId { get; set; }

    public bool? IsCorrect { get; set; }

    public DateTime? AnsweredAt { get; set; }
}
