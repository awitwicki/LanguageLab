using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace LanguageLab.TgBot;

/// <summary>
/// The one thing the bot says. Kept pure — a name and a URL in, text and a keyboard out — so
/// the copy and the button can be tested without Telegram.
/// </summary>
public static class StartMessage
{
    public const string ButtonText = "Open LanguageLab";

    public static string Text(string botName) =>
        $"{botName}\n\n" +
        "Learn the words of the books you read: pick a book, sort out the words you already " +
        "know, and drill the rest with spaced repetition. Irregular verbs and pronunciation " +
        "trainers included.\n\n" +
        "Tap the button below to open the app.";

    /// <summary>
    /// A web_app button, not a url one: only web_app opens the page inside Telegram's web view,
    /// where the SPA reads the signed launch parameters and signs in on its own.
    /// </summary>
    public static InlineKeyboardMarkup Keyboard(string webAppUrl) =>
        new(InlineKeyboardButton.WithWebApp(ButtonText, new WebAppInfo { Url = webAppUrl }));
}
