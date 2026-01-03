using System.Diagnostics;
using System.Reflection;
using LanguageLab.Domain.Interfaces;
using LanguageLab.Infrastructure.Database;
using NLog;
using PowerBot.Lite.Attributes;
using PowerBot.Lite.Handlers;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LanguageLab.TgBot.Handlers;

public class BotHandler : BaseHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IModeratorsService _moderatorsService;
    private readonly ILogger _logger;

    public BotHandler(ApplicationDbContext dbContext, IModeratorsService moderatorsService, ILogger logger)
    {
        _dbContext = dbContext;
        _moderatorsService = moderatorsService;
        _logger = logger;
    }

    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/start$")]
    public async Task Start()
    {
        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

        var startMessageText = @$"LanguageLab bot.
Use command /train to start testing from first dictionary in database.
Use command /list to see all available dictionaries.
Send csv file with word pairs (WITHOUT HEADER) to add new dictionary (only for admins).

`Bot version: {version}`";
        
        await BotClient.SendMessage(chatId: ChatId,
            text: startMessageText,
            parseMode: ParseMode.Markdown);
    }

    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/list$")]
    public async Task ListDictionaries()
    {
        var dictionaries = _dbContext.Dictionaries.ToList();

        if (dictionaries.Count == 0)
        {
            await BotClient.SendMessage(chatId: ChatId,
                text: "No dictionaries found. Please add some first.",
                parseMode: ParseMode.Markdown);
            return;
        }

        var messageText = "Available dictionaries:\n" + string.Join("\n", dictionaries.Select(d => $"- {d.Name} ({d.WordsCount} words)"));

        await BotClient.SendMessage(chatId: ChatId,
            text: messageText,
            parseMode: ParseMode.Markdown);
    }
    
    [MessageReaction(ChatAction.Typing)]
    [MessageTypeFilter(MessageType.Document)]
    public async Task ProcessNewDictionary()
    {
        try
        {
            if (!_moderatorsService.IsUserModerator(User.Id))
            {
                await BotClient.SendMessage(chatId: ChatId,
                    text: "You are not allowed to add new dictionaries",
                    parseMode: ParseMode.Markdown);
                return;
            }

            var document = Message.Document!;

            // Check document size
            if (document.FileSize > 1024 * 1024 * 10)
            {
                await BotClient.SendMessage(chatId: ChatId,
                    text: "File size exceeds the limit of 10 MB",
                    parseMode: ParseMode.Markdown);
                return;
            }

            // Check file extension
            if (document.MimeType != "text/plain")
            {
                await BotClient.SendMessage(chatId: ChatId,
                    text: "Unsupported file format. Only text files are allowed",
                    parseMode: ParseMode.Markdown);
                return;
            }

            // Download file
            var file = await BotClient.GetFile(document.FileId);
            using var memoryStream = new MemoryStream();
            await BotClient.DownloadFile(file.FilePath!, memoryStream);
            memoryStream.Position = 0;

            using var reader = new StreamReader(memoryStream);
            var content = await reader.ReadToEndAsync();

            // Parse content
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var wordPairs = new List<LanguageLab.Domain.Entities.WordPair>();

            foreach (var line in lines)
            {
                var parts = line.Split([','], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    wordPairs.Add(new LanguageLab.Domain.Entities.WordPair
                    {
                        Word = parts[0].Trim(),
                        Translation = parts[1].Trim()
                    });
                }
            }

            if (wordPairs.Count == 0)
            {
                await BotClient.SendMessage(ChatId, "Не вдалося знайти жодної пари слів у файлі.");
                return;
            }


            // 1. Get all unique words from the file to minimize DB queries
            var uniqueWords = wordPairs.Select(p => p.Word).Distinct().ToList();

            // 2. Fetch existing word pairs from DB that match the incoming words
            var existingWordPairs = _dbContext.Words
                .Where(wp => uniqueWords.Contains(wp.Word))
                .ToList();

            var finalWordPairs = new List<Domain.Entities.WordPair>();

            foreach (var incoming in wordPairs)
            {
                // 3. Try to find if this specific Word + Translation combo already exists
                var existing = existingWordPairs.FirstOrDefault(wp =>
                    wp.Word.Equals(incoming.Word, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Use the existing entity
                    finalWordPairs.Add(existing);
                }
                else
                {
                    // Create a new entity
                    finalWordPairs.Add(new LanguageLab.Domain.Entities.WordPair
                    {
                        Word = incoming.Word,
                        Translation = incoming.Translation
                    });
                }
            }
            
            // find duplicates
            finalWordPairs.GroupBy(x => x.Word).Where(g => g.Count() > 1).ToList().ForEach(x => _logger.Warn($"Duplicate word found: {x.Key}"));

            // Create dictionary
            var dictionary = new LanguageLab.Domain.Entities.Dictionary
            {
                Name = document.FileName?.Replace(".txt", "") ?? "Новий невідомий словник",
                WordsCount = wordPairs.Count,
                Words = finalWordPairs
            };

            _dbContext.Dictionaries.Add(dictionary);
            await _dbContext.SaveChangesAsync();

            await BotClient.SendMessage(ChatId,
                $"Словник '{dictionary.Name}' успішно створено! Додано {wordPairs.Count} слів.");
        }
        catch (Exception e)
        {
            _logger.Error(e, e.Message);
            await BotClient.SendMessage(ChatId, "Помилка при створенні словника.");
        }
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
