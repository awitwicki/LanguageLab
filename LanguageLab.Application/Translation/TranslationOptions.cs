namespace LanguageLab.Application.Translation;

/// <summary>Bound from the "Translation" configuration section (Translation__* in Docker).</summary>
public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    /// <summary>
    /// Optional. MyMemory allows 5 000 characters a day per IP anonymously and 50 000 when any
    /// contact email rides along in the request. Never shown to users.
    /// </summary>
    public string? MyMemoryEmail { get; set; }

    /// <summary>
    /// Optional. DeepL API Free key (it ends in ":fx"), for sentence translation in the reader:
    /// 500 000 characters a month for the whole app. Without it the reader hides the button.
    /// </summary>
    public string? DeepLApiKey { get; set; }
}
