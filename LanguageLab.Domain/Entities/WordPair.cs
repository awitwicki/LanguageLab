using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A word, translated per learner language in <see cref="Translations"/>. Shared rows (Owner ==
/// null) are the global vocabulary: one row per word, reused across dictionaries, which is what
/// lets the "know" / "don't know" shelves survive the import of the next book. Owned rows belong
/// to one user's personal dictionary — the user's translation never touches the shared one, and
/// their shelves for it are independent of the same word met in a book. A word may have no
/// translation at all in a given language; that is the absence of a row, not an empty one.
/// </summary>
public class WordPair : BaseEntity
{
    public required string Word { get; set; }

    /// <summary>Null = shared vocabulary. Set only on words a user typed into their personal dictionary.</summary>
    public TelegramUser? Owner { get; set; }
    [ForeignKey(nameof(Owner))]
    public long? OwnerId { get; set; }

    public IList<Dictionary> Dictionaries { get; set; } = new List<Dictionary>();

    /// <summary>
    /// Never read this navigation in memory without filtering by language — query through
    /// <c>TranslationQueries</c> or an explicit <c>.Where(t => t.Language == ...)</c> predicate
    /// instead. A tracked <see cref="WordPair"/> can carry rows for more than one language at
    /// once (e.g. looked up earlier in the same <c>DbContext</c>), so an unfiltered
    /// <c>FirstOrDefault()</c> can silently return the wrong language's translation.
    /// </summary>
    public IList<WordTranslation> Translations { get; set; } = new List<WordTranslation>();
}
