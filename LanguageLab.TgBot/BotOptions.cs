using Microsoft.Extensions.Configuration;

namespace LanguageLab.TgBot;

/// <summary>
/// Everything the bot needs from configuration. Telegram:BotToken is the same token the API
/// checks Mini App sign-ins with; WebApp:Url is the public address of the SPA, which both the
/// /start button and the chat menu button open inside Telegram.
/// </summary>
public sealed record BotOptions(string BotToken, string WebAppUrl)
{
    /// <summary>
    /// Read once, at startup, so a missing key fails with its name rather than surfacing as a
    /// Bot API error on somebody's first /start.
    /// </summary>
    public static BotOptions Read(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        var url = configuration["WebApp:Url"];

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(token))
        {
            missing.Add("Telegram:BotToken");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            missing.Add("WebApp:Url");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing configuration: {string.Join(", ", missing)} " +
                "(Telegram__BotToken / WebApp__Url as environment variables in Docker). " +
                "The token comes from @BotFather; the URL is where the web app is served.");
        }

        // Telegram opens a Mini App only over https, and reports anything else as a Bot API
        // error when the button is sent — so the check is made here, where the message is clear.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"WebApp:Url must be an https:// address, got '{url}'. " +
                "Telegram does not open a Mini App over plain http.");
        }

        return new BotOptions(token!, url!);
    }
}
