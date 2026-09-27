namespace LanguageLab.Domain.Languages;

/// <summary>
/// A language a learner can pick as their main one — the target of every translation they see.
/// Code is what is stored (TelegramUser.Language, WordTranslation.Language, Training.Language);
/// the two provider codes are what DeepL's target_lang and MyMemory's langpair expect.
/// </summary>
public sealed record LearnerLanguage(
    string Code, string EnglishName, string NativeName, string DeepLCode, string MyMemoryCode);

/// <summary>
/// Code-only, like the verb and pronunciation catalogs: DeepL's classic target set plus
/// Filipino, minus Russian by the product owner's decision, minus English (the source).
/// Ukrainian first — it is the default, and every row that predates the catalog is Ukrainian.
/// </summary>
public static class LearnerLanguages
{
    public const string DefaultCode = "uk";
    public const int CodeMaxLength = 8;

    public static readonly IReadOnlyList<LearnerLanguage> All =
    [
        new("uk", "Ukrainian", "Українська", "UK", "uk"),
        new("ar", "Arabic", "العربية", "AR", "ar"),
        new("bg", "Bulgarian", "Български", "BG", "bg"),
        new("zh", "Chinese (Simplified)", "中文（简体）", "ZH-HANS", "zh-CN"),
        new("cs", "Czech", "Čeština", "CS", "cs"),
        new("da", "Danish", "Dansk", "DA", "da"),
        new("nl", "Dutch", "Nederlands", "NL", "nl"),
        new("et", "Estonian", "Eesti", "ET", "et"),
        new("tl", "Filipino", "Filipino", "TL", "tl"),
        new("fi", "Finnish", "Suomi", "FI", "fi"),
        new("fr", "French", "Français", "FR", "fr"),
        new("de", "German", "Deutsch", "DE", "de"),
        new("el", "Greek", "Ελληνικά", "EL", "el"),
        new("he", "Hebrew", "עברית", "HE", "he"),
        new("hu", "Hungarian", "Magyar", "HU", "hu"),
        new("id", "Indonesian", "Bahasa Indonesia", "ID", "id"),
        new("it", "Italian", "Italiano", "IT", "it"),
        new("ja", "Japanese", "日本語", "JA", "ja"),
        new("ko", "Korean", "한국어", "KO", "ko"),
        new("lv", "Latvian", "Latviešu", "LV", "lv"),
        new("lt", "Lithuanian", "Lietuvių", "LT", "lt"),
        new("nb", "Norwegian (Bokmål)", "Norsk bokmål", "NB", "no"),
        new("pl", "Polish", "Polski", "PL", "pl"),
        new("pt", "Portuguese (Brazil)", "Português (Brasil)", "PT-BR", "pt-BR"),
        new("ro", "Romanian", "Română", "RO", "ro"),
        new("sk", "Slovak", "Slovenčina", "SK", "sk"),
        new("sl", "Slovenian", "Slovenščina", "SL", "sl"),
        new("es", "Spanish", "Español", "ES", "es"),
        new("sv", "Swedish", "Svenska", "SV", "sv"),
        new("th", "Thai", "ไทย", "TH", "th"),
        new("tr", "Turkish", "Türkçe", "TR", "tr"),
        new("vi", "Vietnamese", "Tiếng Việt", "VI", "vi"),
    ];

    public static LearnerLanguage Default => All[0];

    /// <summary>Exact, lowercase match on Code. Null for anything else — "ru" included.</summary>
    public static LearnerLanguage? Find(string? code) =>
        code == null ? null : All.FirstOrDefault(l => l.Code == code);

    /// <summary>
    /// Telegram's language_code is an IETF tag ("en", "pt-br", sometimes "fil"). Only a
    /// suggestion for the picker — never stored as the learner's language on its own.
    /// </summary>
    public static LearnerLanguage? FromTelegram(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var primary = languageCode.Trim().ToLowerInvariant().Split('-', '_')[0];

        return Find(primary switch
        {
            "fil" => "tl",
            "no" => "nb",
            _ => primary,
        });
    }
}
