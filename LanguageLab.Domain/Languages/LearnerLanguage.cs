namespace LanguageLab.Domain.Languages;

/// <summary>
/// A language a learner can pick as their main one — the target of every translation they see.
/// Code is what is stored (TelegramUser.Language, WordTranslation.Language, Training.Language);
/// EnglishName is what the translation prompts name.
/// </summary>
public sealed record LearnerLanguage(string Code, string EnglishName, string NativeName);

/// <summary>
/// Code-only, like the verb and pronunciation catalogs: DeepL's classic target set (where the
/// list started) plus Filipino, minus Russian by the product owner's decision, minus English (the
/// source). Ukrainian first — it is the default, and every row that predates the catalog is Ukrainian.
/// </summary>
public static class LearnerLanguages
{
    public const string DefaultCode = "uk";
    public const int CodeMaxLength = 8;

    public static readonly IReadOnlyList<LearnerLanguage> All =
    [
        new("uk", "Ukrainian", "Українська"),
        new("ar", "Arabic", "العربية"),
        new("bg", "Bulgarian", "Български"),
        new("zh", "Chinese (Simplified)", "中文（简体）"),
        new("cs", "Czech", "Čeština"),
        new("da", "Danish", "Dansk"),
        new("nl", "Dutch", "Nederlands"),
        new("et", "Estonian", "Eesti"),
        new("tl", "Filipino", "Filipino"),
        new("fi", "Finnish", "Suomi"),
        new("fr", "French", "Français"),
        new("de", "German", "Deutsch"),
        new("el", "Greek", "Ελληνικά"),
        new("he", "Hebrew", "עברית"),
        new("hu", "Hungarian", "Magyar"),
        new("id", "Indonesian", "Bahasa Indonesia"),
        new("it", "Italian", "Italiano"),
        new("ja", "Japanese", "日本語"),
        new("ko", "Korean", "한국어"),
        new("lv", "Latvian", "Latviešu"),
        new("lt", "Lithuanian", "Lietuvių"),
        new("nb", "Norwegian (Bokmål)", "Norsk bokmål"),
        new("pl", "Polish", "Polski"),
        new("pt", "Portuguese (Brazil)", "Português (Brasil)"),
        new("ro", "Romanian", "Română"),
        new("sk", "Slovak", "Slovenčina"),
        new("sl", "Slovenian", "Slovenščina"),
        new("es", "Spanish", "Español"),
        new("sv", "Swedish", "Svenska"),
        new("th", "Thai", "ไทย"),
        new("tr", "Turkish", "Türkçe"),
        new("vi", "Vietnamese", "Tiếng Việt"),
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
