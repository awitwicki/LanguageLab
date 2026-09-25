using LanguageLab.TgBot;
using Microsoft.Extensions.Configuration;

namespace LanguageLab.Tests;

public class BotOptionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Fact]
    public void Reads_the_token_and_the_app_url()
    {
        var options = BotOptions.Read(
            Config(("Telegram:BotToken", "123:abc"), ("WebApp:Url", "https://l.kodzuverse.com")));

        Assert.Equal("123:abc", options.BotToken);
        Assert.Equal("https://l.kodzuverse.com", options.WebAppUrl);
    }

    [Fact]
    public void Names_every_missing_key_at_once()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BotOptions.Read(Config()));

        Assert.Contains("Telegram:BotToken", error.Message);
        Assert.Contains("WebApp:Url", error.Message);
    }

    // Telegram refuses a Mini App over plain http, and that refusal would only show up as a
    // Bot API error on somebody's first /start — so it is caught here, at startup.
    [Theory]
    [InlineData("http://l.kodzuverse.com")]
    [InlineData("l.kodzuverse.com")]
    public void Refuses_an_app_url_that_is_not_https(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BotOptions.Read(Config(("Telegram:BotToken", "123:abc"), ("WebApp:Url", url))));

        Assert.Contains("https://", error.Message);
    }
}
