using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace LanguageLab.TgBot;

/// <summary>
/// Long-polls Telegram and answers every private message with the start message. There is
/// nothing else to handle: the app itself lives in the web view the button opens.
/// </summary>
public sealed class BotService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly BotOptions _options;
    private readonly ILogger<BotService> _logger;

    // Filled in from GetMe at startup, so renaming the bot in @BotFather renames the greeting.
    private string _botName = "LanguageLab";

    public BotService(ITelegramBotClient bot, BotOptions options, ILogger<BotService> logger)
    {
        _bot = bot;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A bad token fails right here, and BackgroundService stops the host with the error.
        var me = await _bot.GetMe(stoppingToken);
        _botName = me.FirstName;
        _logger.LogInformation("Running as @{Username}", me.Username);

        await _bot.SetMyCommands(
            [new BotCommand { Command = "start", Description = StartMessage.ButtonText }],
            cancellationToken: stoppingToken);

        // The chat's menu button opens the app too, so the user never has to scroll back to
        // the /start reply. Set at every start: it is idempotent, and it follows a URL change.
        await _bot.SetChatMenuButton(
            menuButton: new MenuButtonWebApp
            {
                Text = StartMessage.ButtonText,
                WebApp = new WebAppInfo { Url = _options.WebAppUrl },
            },
            cancellationToken: stoppingToken);

        await _bot.ReceiveAsync(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message] },
            stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        // Groups and channels are ignored: the button only makes sense in a private chat.
        if (update.Message is not { Chat.Type: ChatType.Private } message)
        {
            return;
        }

        try
        {
            await bot.SendMessage(
                message.Chat.Id,
                StartMessage.Text(_botName),
                replyMarkup: StartMessage.Keyboard(_options.WebAppUrl),
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A blocked bot or a flood limit must not take polling down with it.
            _logger.LogWarning(exception, "Could not reply in chat {ChatId}", message.Chat.Id);
        }
    }

    private async Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        // Telegram.Bot calls back in here and resumes polling when this returns; the pause
        // keeps a dead network from turning into a tight loop of failures.
        _logger.LogError(exception, "Polling failed; retrying in 5 seconds");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}
