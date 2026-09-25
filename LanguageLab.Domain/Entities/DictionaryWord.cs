namespace LanguageLab.Domain.Entities;

/// <summary>
/// The "dictionary × word" join with payload. The table keeps the name the
/// convention used to generate (DictionaryWords), so the migration only adds
/// a column and existing rows stay where they are.
/// </summary>
public class DictionaryWord
{
    public Dictionary Dictionary { get; set; } = null!;
    public long DictionaryId { get; set; }

    public WordPair WordPair { get; set; } = null!;
    public long WordPairId { get; set; }

    /// <summary>
    /// The sum of Count over all chapters. Stored rather than computed by a query:
    /// flat imports have no chapters, and without this field the sorting queue
    /// would need two different branches instead of one.
    /// 0 means "frequency unknown" — that is what dictionaries loaded through the bot look like.
    /// </summary>
    public int Frequency { get; set; }
}
