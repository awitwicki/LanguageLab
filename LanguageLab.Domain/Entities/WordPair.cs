using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A "word — translation" pair. Shared rows (Owner == null) are the global vocabulary: one
/// row per word, reused across dictionaries, which is what lets the "know" / "don't know"
/// shelves survive the import of the next book. Owned rows belong to one user's personal
/// dictionary — the user's translation never touches the shared one, and their shelves for
/// it are independent of the same word met in a book. Translation may be empty on words
/// imported without one.
/// </summary>
public class WordPair : BaseEntity
{
    public required string Word { get; set; }
    public required string Translation { get; set; }

    /// <summary>Null = shared vocabulary. Set only on words a user typed into their personal dictionary.</summary>
    public TelegramUser? Owner { get; set; }
    [ForeignKey(nameof(Owner))]
    public long? OwnerId { get; set; }

    public IList<Dictionary> Dictionaries { get; set; } = new List<Dictionary>();
}
