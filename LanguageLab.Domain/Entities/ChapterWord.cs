namespace LanguageLab.Domain.Entities;

/// <summary>
/// How many times a word occurs in a particular chapter. The key is composite,
/// there is no separate Id — a book has tens of thousands of these rows, and no
/// navigation ever refers to one of them individually.
/// </summary>
public class ChapterWord
{
    public Chapter Chapter { get; set; } = null!;
    public long ChapterId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    public long WordPairId { get; set; }

    public int Count { get; set; }
}
