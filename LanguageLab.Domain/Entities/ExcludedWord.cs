using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// Третя полиця поруч із KnownWord і UnknownWord: слово, яке юзер прибрав
/// зі списку назавжди (імена, сміття після лематизації). Глобальна —
/// виключене слово не вилазить і в наступних книжках.
/// </summary>
public class ExcludedWord : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    public DateTime CreatedAt { get; set; }
}
