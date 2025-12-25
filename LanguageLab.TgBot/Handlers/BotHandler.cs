using System.Diagnostics;
using System.Reflection;
using LanguageLab.Infrastructure.Database;
using PowerBot.Lite.Attributes;
using PowerBot.Lite.Handlers;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LanguageLab.TgBot.Handlers;

public class BotHandler : BaseHandler
{
    // private ApplicationDbContext _dbContext;

    // public BotHandler(ApplicationDbContext dbContext)
    // {
    //     _dbContext = dbContext;
    // }

    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/start$")]
    public async Task Start()
    {
        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

        var startMessageText = @$"LanguageLab bot.
Use command /train to start testing from first dictionary in database.

`Версія: {version}`";
        
        await BotClient.SendMessage(chatId: ChatId,
            text: startMessageText,
            parseMode: ParseMode.Markdown);
    }
    
    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/train")]
    public async Task Train()
    {
        var wordId = 5;
        var word = "cat";
        var translation = "кіт";
        var translations = new List<string> { translation, "пес", "авто", "телефон", "місто", "криниця" }.Shuffle().ToList();
        
        var messageText = @$"Вибери правильний варіант для слова:
**Cat**";

        var keyboardMarkup = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>> {
            new List<InlineKeyboardButton> {
                InlineKeyboardButton.WithCallbackData(translations[0], $"train_true_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[1], $"train_false_{wordId}"),
            },
            new List<InlineKeyboardButton> {
                InlineKeyboardButton.WithCallbackData(translations[2], $"train_false_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[3], $"train_false_{wordId}"),
            },
            new List<InlineKeyboardButton> {
                InlineKeyboardButton.WithCallbackData(translations[4], $"train_false_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[5], $"train_false_{wordId}"),
            }
        });

        await BotClient.SendMessage(chatId: ChatId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }
    
    [MessageReaction(ChatAction.Typing)]
    [CallbackQueryHandler("^train_")]
    public async Task DictWordClicked()
    {
        await BotClient.EditMessageReplyMarkup(ChatId, MessageId, null);
        
        // Parse user id
        var oldWordId = long.Parse(CallbackQuery.Data!.Split('_').Last());

        // Parse result
        var result = CallbackQuery.Data
            .Split('_')[1] == "true";
        var resultStr = result ? "Правильно" : "Неправильно";

        var wordId = 5;
        var word = "cat";
        var translation = "кіт";
        var translations = new List<string> { translation, "пес", "авто", "телефон", "місто", "криниця" }.Shuffle().ToList();

        var messageText = @$"{resultStr}

Вибери правильний варіант для слова:
**Cat**";

        var keyboardMarkup = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>> {
            new () {
                InlineKeyboardButton.WithCallbackData(translations[0], $"train_true_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[1], $"train_false_{wordId}"),
            },
            new () {
                InlineKeyboardButton.WithCallbackData(translations[2], $"train_false_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[3], $"train_false_{wordId}"),
            },
            new () {
                InlineKeyboardButton.WithCallbackData(translations[4], $"train_false_{wordId}"),
                InlineKeyboardButton.WithCallbackData(translations[5], $"train_false_{wordId}"),
            }
        });

        await BotClient.SendMessage(chatId: ChatId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }
}
