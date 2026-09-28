using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// One dictionary's missing translations into one learner language, filled in the background by
/// the translation worker. Unique per (DictionaryId, Language). A pass walks the dictionary's
/// shared words once, most frequent first, the cursor marking how far it got; a word the model
/// gave nothing for is passed over until the next enqueue re-arms the job.
/// </summary>
public class TranslationJob : BaseEntity
{
    public Dictionary Dictionary { get; set; } = null!;
    [ForeignKey(nameof(Dictionary))]
    public long DictionaryId { get; set; }

    /// <summary>A LearnerLanguages code.</summary>
    public required string Language { get; set; }

    public TranslationJobStatus Status { get; set; }

    /// <summary>Words with no translation in <see cref="Language"/> when the job was last (re)armed.</summary>
    public int Total { get; set; }

    /// <summary>Words this pass has moved past, translated or not.</summary>
    public int Done { get; set; }

    /// <summary>
    /// Keyset cursor: the DictionaryWord.Frequency and WordPairId of the last word the pass moved
    /// past. Both null = the start of the pass.
    /// </summary>
    public int? CursorFrequency { get; set; }

    public long? CursorWordPairId { get; set; }

    /// <summary>Unavailable batches in a row (quota refusals do not count); any successful batch resets it.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>UTC. The round-robin key: the pending job processed longest ago goes next, a never-processed one first.</summary>
    public DateTime? LastProcessedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
