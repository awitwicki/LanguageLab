using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// The last time the user sorted words in one scope — a whole book (ChapterId null) or a
/// single chapter. The shelves record which word was marked and when, never where the user
/// was standing when they marked it: one word sits in several books, so the scope cannot be
/// recovered from KnownWord and friends afterwards. Written here instead, so the home screen
/// can offer a way back into sorting that was left unfinished.
///
/// Server-side for the same reason the sorting undo is (<see cref="KnownWord.CreatedAt"/>):
/// it has to survive a reload and follow the user to their other devices.
/// </summary>
public class SortingVisit : BaseEntity
{
    public TelegramUser User { get; set; } = null!;
    [ForeignKey(nameof(User))]
    public long UserId { get; set; }

    public Dictionary Dictionary { get; set; } = null!;
    [ForeignKey(nameof(Dictionary))]
    public long DictionaryId { get; set; }

    /// <summary>null — the user was sorting the whole book rather than one of its chapters.</summary>
    public Chapter? Chapter { get; set; }
    [ForeignKey(nameof(Chapter))]
    public long? ChapterId { get; set; }

    /// <summary>UTC, the most recent mark made in this scope. The row is upserted, never appended to.</summary>
    public DateTime LastSortedAt { get; set; }
}
