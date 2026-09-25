using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A chapter of a book. Exists only for dictionaries loaded from fb2: flat imports
/// (like "top 500 English words") have no chapters at all, and it is precisely the
/// absence of chapters that tells them apart from a book — there is no separate flag.
/// </summary>
public class Chapter : BaseEntity
{
    public Dictionary Dictionary { get; set; } = null!;
    [ForeignKey(nameof(Dictionary))]
    public long DictionaryId { get; set; }

    /// <summary>Position in the book, 0-based.</summary>
    public int Order { get; set; }

    /// <summary>Empty when the fb2 section had no &lt;title&gt;.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Unique base forms in the chapter.</summary>
    public int WordsCount { get; set; }

    public IList<ChapterWord> Words { get; set; } = new List<ChapterWord>();
}
