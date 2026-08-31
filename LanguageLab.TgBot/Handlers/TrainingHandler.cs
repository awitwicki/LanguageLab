using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Interfaces;
using LanguageLab.Infrastructure.Database;
using LanguageLab.TgBot.Services;
using Microsoft.EntityFrameworkCore;
using NLog;
using PowerBot.Lite.Attributes;
using PowerBot.Lite.Handlers;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LanguageLab.TgBot.Handlers;

/// <summary>
/// Тонкий шар над TrainingSessionService: розбирає callback_data, малює повідомлення
/// й редагує їх на місці. Жодної логіки навчання тут немає.
/// </summary>
public class TrainingHandler : BaseHandler
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TrainingSessionService _sessions;
    private readonly WordSelectionService _selection;
    private readonly IModeratorsService _moderatorsService;
    private readonly ILogger _logger;

    public TrainingHandler(
        ApplicationDbContext dbContext,
        TrainingSessionService sessions,
        WordSelectionService selection,
        IModeratorsService moderatorsService,
        ILogger logger)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _selection = selection;
        _moderatorsService = moderatorsService;
        _logger = logger;
    }

    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/train$")]
    public async Task Train()
    {
        var user = await _sessions.GetOrCreateUserAsync(User.Id);
        var dictionary = await _dbContext.Dictionaries.OrderBy(d => d.Id).FirstOrDefaultAsync();

        if (dictionary == null)
        {
            await BotClient.SendMessage(ChatId,
                "Словників немає. Надішли .txt файл із рядками «слово,переклад», щоб додати перший.",
                parseMode: ParseMode.Html);
            return;
        }

        await ShowMenuAsync(user.Id, dictionary, edit: false);
    }

    [MessageReaction(ChatAction.Typing)]
    [MessageHandler("^/stats$")]
    public async Task StatsCommand()
    {
        var user = await _sessions.GetOrCreateUserAsync(User.Id);
        var stats = await _sessions.GetStatsAsync(user.Id, DateTime.UtcNow);

        await BotClient.SendMessage(chatId: ChatId,
            text: TrainingViews.StatsText(stats),
            parseMode: ParseMode.Html);
    }

    [CallbackQueryHandler("^tstats$")]
    public async Task StatsClicked()
    {
        var user = await _sessions.GetOrCreateUserAsync(User.Id);
        var stats = await _sessions.GetStatsAsync(user.Id, DateTime.UtcNow);

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: TrainingViews.StatsText(stats),
            replyMarkup: null,
            parseMode: ParseMode.Html);
    }

    [CallbackQueryHandler("^tdicts$")]
    public async Task ChooseDictionary()
    {
        var dictionaries = await _dbContext.Dictionaries.OrderBy(d => d.Id).ToListAsync();

        var rows = dictionaries
            .Select(d => new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"{d.Name} ({d.WordsCount} слів)", $"tdict_{d.Id}")
            })
            .ToList();

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: "Вибери словник:",
            replyMarkup: new InlineKeyboardMarkup(rows),
            parseMode: ParseMode.Html);
    }

    [CallbackQueryHandler("^tdict_")]
    public async Task DictionaryChosen()
    {
        var dictionaryId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        var user = await _sessions.GetOrCreateUserAsync(User.Id);
        var dictionary = await _dbContext.Dictionaries.FirstAsync(d => d.Id == dictionaryId);

        await ShowMenuAsync(user.Id, dictionary, edit: true);
    }

    [CallbackQueryHandler("^tnew_")]
    public async Task StartNewBatch()
    {
        var dictionaryId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        var user = await _sessions.GetOrCreateUserAsync(User.Id);

        var training = await _sessions.StartNewBatchAsync(user.Id, dictionaryId, DateTime.UtcNow);

        if (training == null)
        {
            await EditPlainAsync("Нових слів у цьому словнику немає. Познач ще слова через /sort або візьми закріплення.");
            return;
        }

        await ShowCardsAsync(training.Id);
    }

    [CallbackQueryHandler("^tcards_")]
    public async Task StartQuiz()
    {
        var trainingId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        await ShowNextQuestionAsync(trainingId, header: null);
    }

    [CallbackQueryHandler("^treview$")]
    public async Task StartReview()
    {
        var user = await _sessions.GetOrCreateUserAsync(User.Id);
        var training = await _sessions.StartReviewAsync(user.Id, DateTime.UtcNow);

        if (training == null)
        {
            await EditPlainAsync("На сьогодні повторювати нічого. Візьми новий батч.");
            return;
        }

        // У закріпленні фази карток немає — ці слова вже бачені.
        await ShowNextQuestionAsync(training.Id, header: null);
    }

    [CallbackQueryHandler("^tretry_")]
    public async Task RetryFailed()
    {
        var previousTrainingId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        var user = await _sessions.GetOrCreateUserAsync(User.Id);

        var training = await _sessions.StartRetryAsync(user.Id, previousTrainingId, DateTime.UtcNow);

        if (training == null)
        {
            await EditPlainAsync("Помилок для повторення немає.");
            return;
        }

        await ShowCardsAsync(training.Id);
    }

    [CallbackQueryHandler("^tq_")]
    public async Task AnswerQuestion()
    {
        var parts = CallbackQuery.Data!.Split('_');
        var questionId = long.Parse(parts[1]);
        var pickedWordPairId = long.Parse(parts[2]);

        var trainingId = await GetTrainingIdAsync(questionId);

        if (trainingId == null)
        {
            return;
        }

        var outcome = await _sessions.AnswerAsync(questionId, pickedWordPairId, DateTime.UtcNow);

        // null = повторний клік по вже відповіданому питанню; просто показуємо поточний стан.
        var header = outcome == null ? null : TrainingViews.AnswerHeader(outcome.IsCorrect, outcome.Word);

        await ShowNextQuestionAsync(trainingId.Value, header);
    }

    [CallbackQueryHandler("^tknow_")]
    public async Task MarkKnown()
    {
        var questionId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        var trainingId = await GetTrainingIdAsync(questionId);

        if (trainingId == null)
        {
            return;
        }

        var word = await _sessions.MarkKnownAsync(questionId, DateTime.UtcNow);
        var header = word == null
            ? null
            : $"✅ <b>{TrainingViews.Escape(word.Word)}</b> більше не з'явиться";

        await ShowNextQuestionAsync(trainingId.Value, header);
    }

    [CallbackQueryHandler("^tdel_")]
    public async Task DeleteWord()
    {
        if (!_moderatorsService.IsUserModerator(User.Id))
        {
            await BotClient.SendMessage(ChatId, "Видаляти слова може лише модератор.", parseMode: ParseMode.Html);
            return;
        }

        var questionId = long.Parse(CallbackQuery.Data!.Split('_').Last());
        var trainingId = await GetTrainingIdAsync(questionId);

        if (trainingId == null)
        {
            return;
        }

        var deleted = await _sessions.DeleteWordAsync(questionId);
        _logger.Info($"Word deleted by {User.Id}: {deleted}");

        var header = deleted == null
            ? null
            : $"🗑 <b>{TrainingViews.Escape(deleted)}</b> видалено назавжди";

        await ShowNextQuestionAsync(trainingId.Value, header);
    }

    [CallbackQueryHandler("^tstop$")]
    public async Task Stop()
    {
        await IgnoreUnmodifiedAsync(() => BotClient.EditMessageReplyMarkup(ChatId, MessageId, null));
        await BotClient.SendMessage(ChatId, "Готово. /train — коли захочеш продовжити.", parseMode: ParseMode.Html);
    }

    private async Task ShowMenuAsync(long userId, LanguageLab.Domain.Entities.Dictionary dictionary, bool edit)
    {
        var learnable = await _selection.CountLearnableAsync(userId, dictionary.Id);
        var due = await _selection.CountDueAsync(userId, DateTime.UtcNow);

        var text = TrainingViews.Menu(dictionary.Name, learnable, due);
        var markup = TrainingViews.MenuKeyboard(dictionary.Id, due);

        if (edit)
        {
            await BotClient.EditMessageText(chatId: ChatId,
                messageId: MessageId,
                text: text,
                replyMarkup: markup,
                parseMode: ParseMode.Html);
            return;
        }

        await BotClient.SendMessage(chatId: ChatId,
            text: text,
            replyMarkup: markup,
            parseMode: ParseMode.Html);
    }

    private async Task ShowCardsAsync(long trainingId)
    {
        var wordIds = await _dbContext.TrainingQuestions
            .Where(q => q.TrainingId == trainingId)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();

        var words = await _dbContext.Words
            .Where(w => wordIds.Contains(w.Id))
            .OrderBy(w => w.Word)
            .ToListAsync();

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: TrainingViews.CardsText(words),
            replyMarkup: TrainingViews.CardsKeyboard(trainingId),
            parseMode: ParseMode.Html);
    }

    private async Task ShowNextQuestionAsync(long trainingId, string? header)
    {
        var question = await _sessions.GetNextQuestionAsync(trainingId);

        if (question == null)
        {
            await ShowSummaryAsync(trainingId);
            return;
        }

        var total = await _dbContext.TrainingQuestions.CountAsync(q => q.TrainingId == trainingId);
        var answered = await _dbContext.TrainingQuestions.CountAsync(q => q.TrainingId == trainingId && q.IsCorrect != null);
        var options = await LoadOptionsAsync(question);

        await BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: TrainingViews.QuestionText(answered + 1, total, header, question),
            replyMarkup: TrainingViews.QuestionKeyboard(question, options, _moderatorsService.IsUserModerator(User.Id)),
            parseMode: ParseMode.Html);
    }

    private async Task ShowSummaryAsync(long trainingId)
    {
        var now = DateTime.UtcNow;
        var training = await _dbContext.Trainings.FirstAsync(t => t.Id == trainingId);
        var summary = await _sessions.FinishAsync(trainingId, now);

        if (summary.Total == 0)
        {
            await EditPlainAsync("Сесія завершена — жодного питання не лишилось.");
            return;
        }

        var due = await _selection.CountDueAsync(training.UserId, now);

        await IgnoreUnmodifiedAsync(() => BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: TrainingViews.SummaryText(summary, now),
            replyMarkup: TrainingViews.SummaryKeyboard(summary, trainingId, training.DictionaryId, due),
            parseMode: ParseMode.Html));
    }

    /// <summary>
    /// Варіант міг бути видалений кнопкою «🗑 Видалити» вже після генерації черги:
    /// OptionIds — звичайний bigint[] без зовнішнього ключа. Зниклі id пропускаємо,
    /// а правильну відповідь підставляємо назад, якщо її не лишилось.
    /// </summary>
    private async Task<List<WordPair>> LoadOptionsAsync(TrainingQuestion question)
    {
        var found = await _dbContext.Words
            .Where(w => question.OptionIds.Contains(w.Id))
            .ToListAsync();

        var ordered = question.OptionIds
            .Select(id => found.FirstOrDefault(w => w.Id == id))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();

        if (ordered.All(w => w.Id != question.WordPairId))
        {
            ordered.Insert(0, question.WordPair);
        }

        return ordered;
    }

    private async Task<long?> GetTrainingIdAsync(long questionId)
    {
        var trainingIds = await _dbContext.TrainingQuestions
            .AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => q.TrainingId)
            .ToListAsync();

        // Питання могло зникнути разом зі словом або бути знятим кнопкою «Знаю».
        return trainingIds.Count == 0 ? null : trainingIds[0];
    }

    private Task EditPlainAsync(string text) =>
        BotClient.EditMessageText(chatId: ChatId,
            messageId: MessageId,
            text: text,
            replyMarkup: null,
            parseMode: ParseMode.Html);

    /// <summary>
    /// Телеграм відхиляє редагування на ідентичний вміст помилкою 400. Це трапляється при
    /// подвійному тапі по вже відпрацьованій кнопці й нічого не ламає, тож глушимо саме її.
    /// Будь-яка інша помилка має летіти далі.
    /// </summary>
    private static async Task IgnoreUnmodifiedAsync(Func<Task> edit)
    {
        try
        {
            await edit();
        }
        catch (ApiRequestException e) when (e.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // Повідомлення вже показує потрібний стан — робити нічого.
        }
    }
}
