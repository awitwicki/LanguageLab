namespace LanguageLab.Application.Translation;

public enum SentenceTranslationStatus
{
    Ok,
    /// <summary>The provider's own quota is gone for this period — not the user's daily limit.</summary>
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
/// English → Ukrainian, one sentence of a book the learner is reading. Never throws. The text
/// belongs to someone's book: an implementation must neither store it nor write it to a log.
/// </summary>
public interface ISentenceTranslator
{
    /// <summary>False without credentials. FallbackSentenceTranslator, the registered one, is always configured.</summary>
    bool IsConfigured { get; }

    Task<SentenceTranslation> TranslateAsync(string text, CancellationToken cancellationToken);
}
