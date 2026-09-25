using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

public class Dictionary : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public int WordsCount { get; set; }

    /// <summary>
    /// Null means a system dictionary: visible to everyone, managed by admins.
    /// Dictionaries imported before accounts existed are all like this, and a
    /// dictionary outlives the account that imported it.
    /// </summary>
    public TelegramUser? Owner { get; set; }
    [ForeignKey(nameof(Owner))]
    public long? OwnerId { get; set; }

    /// <summary>
    /// Published dictionaries are visible to every signed-in user; everything else only to its
    /// owner and (except for personal lists) to admins.
    /// </summary>
    public PublicationStatus PublicationStatus { get; set; } = PublicationStatus.Private;

    /// <summary>
    /// The user's own word list ("My words"): one per user, private, no chapters, words typed
    /// in by hand rather than imported. Visible to its owner only — admins included.
    /// </summary>
    public bool IsPersonal { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the imported book file (ReaderHash), so the reader can link a book
    /// it opens to this dictionary. Null for dictionaries imported before the reader existed and
    /// for flat imports.
    /// </summary>
    [MaxLength(ReaderHash.Length)]
    public string? FileHash { get; set; }

    public IList<WordPair> Words { get; set; } = new List<WordPair>();

    public IList<Chapter> Chapters { get; set; } = new List<Chapter>();
}
