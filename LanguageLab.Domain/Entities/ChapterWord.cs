namespace LanguageLab.Domain.Entities;

/// <summary>
/// Скільки разів слово трапляється в конкретній главі. Ключ складений,
/// окремого Id немає — рядків десятки тисяч на книжку, і жодна навігація
/// на них поодинці не посилається.
/// </summary>
public class ChapterWord
{
    public Chapter Chapter { get; set; } = null!;
    public long ChapterId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    public long WordPairId { get; set; }

    public int Count { get; set; }
}
