using LanguageLab.TgBot;

namespace LanguageLab.Tests;

public class StartMessageTests
{
    [Fact]
    public void Text_opens_with_the_bot_name_and_says_what_the_app_is_for()
    {
        var text = StartMessage.Text("LanguageLab");

        Assert.StartsWith("LanguageLab\n\n", text);
        Assert.Contains("Learn the words of the books you read", text);
        Assert.EndsWith("Tap the button below to open the app.", text);
    }

    // A web_app button, not a url one: only web_app opens the page inside Telegram's own web
    // view, where the SPA can read the signed launch parameters and sign in without a browser.
    [Fact]
    public void Keyboard_is_one_web_app_button_pointing_at_the_app()
    {
        var keyboard = StartMessage.Keyboard("https://l.kodzuverse.com");

        var button = Assert.Single(Assert.Single(keyboard.InlineKeyboard));

        Assert.Equal("Open LanguageLab", button.Text);
        Assert.Equal("https://l.kodzuverse.com", button.WebApp?.Url);
        Assert.Null(button.Url);
    }
}
