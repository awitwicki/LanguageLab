using System.Diagnostics;
using System.Reflection;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Interfaces;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
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
        // TODO extract to middleware
        // Register user in db
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.TelegramUserId == Message.From!.Id);
        if (user == null)
        {
            user = new TelegramUser { TelegramUserId = User.Id };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
        }
        
        // Start info stuff
        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

        var startMessageText = @$"LanguageLab bot.
Use command /train to start learning words from a dictionary.
Use command /stats to see your progress.
Use command /list to see all available dictionaries.
Use command /sort to check all words in dictionary to check if you already know them.
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

            // Parse content: ділимо по ПЕРШІЙ комі — у перекладах трапляються свої коми,
            // напр. "long-sleeping,той, що довго спить".
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            // Порівняння за Ordinal, бо унікальний індекс на "Word" у Postgres теж регістрозалежний.
            var parsed = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                var parts = line.Split(',', 2);
                if (parts.Length < 2)
                {
                    continue;
                }

                var parsedWord = parts[0].Trim();
                var parsedTranslation = parts[1].Trim();

                if (parsedWord.Length == 0 || parsedTranslation.Length == 0)
                {
                    continue;
                }

                if (!parsed.TryAdd(parsedWord, parsedTranslation))
                {
                    _logger.Warn($"Duplicate word found: {parsedWord}");
                }
            }

            if (parsed.Count == 0)
            {
                await BotClient.SendMessage(ChatId, "Не вдалося знайти жодної пари слів у файлі.");
                return;
            }

            var incomingWords = parsed.Keys.ToList();

            var existingWords = await _dbContext.Words
                .Where(w => incomingWords.Contains(w.Word))
                .ToListAsync();

            var byWord = existingWords.ToDictionary(w => w.Word, StringComparer.Ordinal);

            var dictionaryWords = new List<WordPair>(parsed.Count);

            foreach (var (word, translation) in parsed)
            {
                if (byWord.TryGetValue(word, out var wordPair))
                {
                    // Орфан, імпортований раніше зі списку known/unknown без перекладу,
                    // нарешті його отримує і стає придатним для навчання.
                    if (string.IsNullOrWhiteSpace(wordPair.Translation))
                    {
                        wordPair.Translation = translation;
                    }
                }
                else
                {
                    wordPair = new WordPair { Word = word, Translation = translation };
                    _dbContext.Words.Add(wordPair);
                    byWord[word] = wordPair;
                }

                dictionaryWords.Add(wordPair);
            }

            var dictionary = new LanguageLab.Domain.Entities.Dictionary
            {
                Name = Path.GetFileNameWithoutExtension(document.FileName) ?? "Новий невідомий словник",
                WordsCount = dictionaryWords.Count,
                Words = dictionaryWords
            };

            _dbContext.Dictionaries.Add(dictionary);
            await _dbContext.SaveChangesAsync();

            await BotClient.SendMessage(ChatId,
                $"Словник '{dictionary.Name}' успішно створено! Додано {dictionaryWords.Count} слів.");
        }
        catch (Exception e)
        {
            _logger.Error(e, e.Message);
            await BotClient.SendMessage(ChatId, "Помилка при створенні словника.");
        }
    }
    
    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/sort")]
    public async Task Sort()
    {
        var dictionaries = await _dbContext.Dictionaries.ToListAsync();

        if (dictionaries.Count == 0)
        {
            await BotClient.SendMessage(ChatId, "Немає словників для сортування.");
            return;
        }

        var messageText = "Вибери словник:";

        var dictButtons = dictionaries.Select(x => new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData($"{x.Name} ({x.WordsCount} слів)", $"sortdict_{x.Id}")
        }).ToList();
        
        var keyboardMarkup = new InlineKeyboardMarkup(dictButtons);

        await BotClient.SendMessage(chatId: ChatId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }

    private async Task<WordPair?> GetNextUnknownWord(long dictionaryId, long telegramUserId)
    {
        var telegramUser = await _dbContext.Users
            .FirstAsync(x => x.TelegramUserId == telegramUserId);

        var knownIds = _dbContext.KnownWords
            .Where(x => x.UserId == telegramUser.Id)
            .Select(x => x.WordPairId);

        var unknownIds = _dbContext.UnknownWords
            .Where(x => x.UserId == telegramUser.Id)
            .Select(x => x.WordPairId);

        var reviewedIds = await knownIds.Union(unknownIds).ToListAsync();

        return await _dbContext.Words
            .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId))
            .Where(w => !reviewedIds.Contains(w.Id))
            .OrderBy(w => w.Id)
            .FirstOrDefaultAsync();
    }
    
    [MessageReaction(ChatAction.Typing)]
    [CallbackQueryHandler("^sortdict_")]
    public async Task SortDictClicked()
    {
        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: "Обознач всі слова словника",
            replyMarkup: null,
            parseMode: ParseMode.Markdown);

        // Parse user id
        var dictId = long.Parse(CallbackQuery.Data!.Split('_').Last());

        var unknownWord = await GetNextUnknownWord(dictId, User.Id);

        if (unknownWord == null)
        {
            var finalText = "Невідомих слів більше немає";

            await BotClient.EditMessageText(chatId: ChatId,
                messageId: MessageId,
                text: finalText,
                replyMarkup: null,
                parseMode: ParseMode.Markdown);

            await BotClient.EditMessageReplyMarkup(ChatId, MessageId, null);
            
            return;
        }
        
        var messageText = $"{unknownWord.Word}";

        var keyboardMarkup = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>> {
            new () {
                InlineKeyboardButton.WithCallbackData("знаю", $"add_known_word_{dictId}_{unknownWord.Id}"),
                InlineKeyboardButton.WithCallbackData("не знаю", $"add_unknown_word_{dictId}_{unknownWord.Id}"),
            }
        });

        await BotClient.SendMessage(chatId: ChatId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }

    [MessageReaction(ChatAction.Typing)]
    [CallbackQueryHandler("^add_known_word_")]
    public async Task AddKnownWordClicked()
    {
        var parts = CallbackQuery.Data!.Split('_');
        var dictionaryId = long.Parse(parts[^2]);
        var wordPairId = long.Parse(parts[^1]);

        var telegramUser = await _dbContext.Users
            .FirstAsync(x => x.TelegramUserId == User.Id);

        if (!_dbContext.KnownWords.Any(x => x.WordPairId == wordPairId && x.UserId == telegramUser.Id))
        {
            _dbContext.KnownWords.Add(new KnownWord { WordPairId = wordPairId, UserId = telegramUser.Id });
            await _dbContext.SaveChangesAsync();
        }

        var unknownWord = await GetNextUnknownWord(dictionaryId, User.Id);

        if (unknownWord == null)
        {
            var finalText = "Невідомих слів більше немає";

            await BotClient.EditMessageText(chatId: ChatId,
                messageId: MessageId,
                text: finalText,
                replyMarkup: null,
                parseMode: ParseMode.Markdown);

            await BotClient.EditMessageReplyMarkup(ChatId, MessageId, null);

            return;
        }

        var messageText = $"{unknownWord.Word}";

        var keyboardMarkup = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>> {
            new () {
                InlineKeyboardButton.WithCallbackData("знаю", $"add_known_word_{dictionaryId}_{unknownWord.Id}"),
                InlineKeyboardButton.WithCallbackData("не знаю", $"add_unknown_word_{dictionaryId}_{unknownWord.Id}"),
            }
        });

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }

    [MessageReaction(ChatAction.Typing)]
    [CallbackQueryHandler("^add_unknown_word_")]
    public async Task AddUnknownWordClicked()
    {
        var parts = CallbackQuery.Data!.Split('_');
        var dictionaryId = long.Parse(parts[^2]);
        var wordPairId = long.Parse(parts[^1]);

        var telegramUser = await _dbContext.Users
            .FirstAsync(x => x.TelegramUserId == User.Id);

        if (!_dbContext.UnknownWords.Any(x => x.WordPairId == wordPairId && x.UserId == telegramUser.Id))
        {
            _dbContext.UnknownWords.Add(new UnknownWord { WordPairId = wordPairId, UserId = telegramUser.Id });
            await _dbContext.SaveChangesAsync();
        }

        var unknownWord = await GetNextUnknownWord(dictionaryId, User.Id);

        if (unknownWord == null)
        {
            var finalText = "Невідомих слів більше немає";

            await BotClient.EditMessageText(chatId: ChatId,
                messageId: MessageId,
                text: finalText,
                replyMarkup: null,
                parseMode: ParseMode.Markdown);

            await BotClient.EditMessageReplyMarkup(ChatId, MessageId, null);

            return;
        }

        var messageText = $"{unknownWord.Word}";

        var keyboardMarkup = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>> {
            new () {
                InlineKeyboardButton.WithCallbackData("знаю", $"add_known_word_{dictionaryId}_{unknownWord.Id}"),
                InlineKeyboardButton.WithCallbackData("не знаю", $"add_unknown_word_{dictionaryId}_{unknownWord.Id}"),
            }
        });

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: messageText,
            replyMarkup: keyboardMarkup,
            parseMode: ParseMode.Markdown);
    }
}
