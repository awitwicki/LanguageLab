using LanguageLab.Domain.Languages;

namespace LanguageLab.Application.Translation;

public enum SentenceTranslationStatus
{
    Ok,
    /// <summary>The provider's own quota is gone for now — not the user's own pacing limit.</summary>
    QuotaExceeded,
    /// <summary>The provider cannot take a text this long.</summary>
    TooLong,
    Failed,
}

public sealed record SentenceTranslation(SentenceTranslationStatus Status, string? Text)
{
    public static readonly SentenceTranslation Quota = new(SentenceTranslationStatus.QuotaExceeded, null);
    public static readonly SentenceTranslation TooLong = new(SentenceTranslationStatus.TooLong, null);
    public static readonly SentenceTranslation Failure = new(SentenceTranslationStatus.Failed, null);

    public static SentenceTranslation Success(string text) => new(SentenceTranslationStatus.Ok, text);
}

/// <summary>
/// English → the learner's language, one sentence of a book the learner is reading. Never throws.
/// The text belongs to someone's book: an implementation must neither store it nor write it to a log.
/// </summary>
public interface ISentenceTranslator
{
    /// <summary>False when no language model is configured (no Translation provider key).</summary>
    bool IsConfigured { get; }

    Task<SentenceTranslation> TranslateAsync(string text, LearnerLanguage target, CancellationToken cancellationToken);
}
