using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// The third shelf next to KnownWord and UnknownWord: a word the user removed
/// from the list for good (names, lemmatization garbage). Global — an excluded
/// word does not show up in later books either.
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
