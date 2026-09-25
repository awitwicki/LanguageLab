using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A chapter the user keeps at hand: listed on the home screen and at the top of the
/// book's page. Per user, like the shelves — one reader's bookmark is nobody else's.
/// Both FKs cascade, so a deleted book or a deleted account takes its stars along.
/// </summary>
public class StarredChapter : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public Chapter Chapter { get; set; } = null!;
    [ForeignKey(nameof(Chapter))]
    public long ChapterId { get; set; }

    /// <summary>UTC. Not shown yet; kept so a "recently starred" order can be offered without a migration.</summary>
    public DateTime CreatedAt { get; set; }
}
