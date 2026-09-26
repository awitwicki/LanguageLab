namespace LanguageLab.Domain.Pronunciation;

/// <summary>
/// Where the committed recordings are served from. The trainer's word clips and the
/// alphabet's sound clips share one directory, so they share one way of addressing it —
/// two places building this path by hand is how the two drift apart.
/// </summary>
public static class PronunciationAudio
{
    public const string UrlPrefix = "/pronunciation-audio/";

    public static string? Url(string? file) => file is null ? null : UrlPrefix + file;
}
