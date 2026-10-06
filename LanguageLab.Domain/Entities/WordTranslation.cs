using System.ComponentModel.DataAnnotations.Schema;

namespace LanguageLab.Domain.Entities;

/// <summary>
/// A word's meaning in one learner language. A row exists only when there is a translation —
/// "untranslated" is the absence of a row, never an empty Text. Unique per (WordPairId,
/// Language), so a shared word carries at most one translation per language, and a personal
/// word keeps its owner's translations in every language they have used.
/// </summary>
public class WordTranslation : BaseEntity
{
    /// <summary>
    /// A translation is a word or a short phrase. Enforced where text comes in (typed, or a model's
    /// answer), not in the schema: migrations run on startup and must not fail on old rows.
    /// </summary>
    public const int MaxTextLength = 200;

    public WordPair WordPair { get; set; } = null!;
    [ForeignKey(nameof(WordPair))]
    public long WordPairId { get; set; }

    /// <summary>A LearnerLanguages code.</summary>
    public required string Language { get; set; }

    public required string Text { get; set; }

    /// <summary>Manual unless the translation provider filled it in, see TranslationService.</summary>
    public TranslationOrigin Origin { get; set; }
}
