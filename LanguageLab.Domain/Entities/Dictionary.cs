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

    /// <summary>Public dictionaries are visible to every signed-in user; private ones only to their owner and admins.</summary>
    public bool IsPublic { get; set; } = true;

    public IList<WordPair> Words { get; set; } = new List<WordPair>();

    public IList<Chapter> Chapters { get; set; } = new List<Chapter>();
}
