using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record AnswerOutcome(bool IsCorrect, WordPair Word);

public sealed record WordResult(
    string Word,
    string Translation,
    int Correct,
    int Total,
    int Box,
    DateTime? DueAt,
    bool IsLearned);

public sealed record TrainingSummary(
    int Correct,
    int Total,
    double Ratio,
    bool Passed,
    IReadOnlyList<WordResult> Words);

public sealed record TrainingStats(
    IReadOnlyList<int> BoxCounts,   // індекс 0 = box 1, довжина = LeitnerScheduler.MaxBox
    int Learned,
    int Known,
    int Due,
    int Correct,
    int Wrong);

/// <summary>
/// Наступне питання для UI: варіанти в порядку OptionIds і лічильники сесії.
/// Question == null означає, що черга вичерпана — лічильники при цьому фінальні.
/// </summary>
public sealed record QuestionView(
    TrainingQuestion? Question,
    IReadOnlyList<WordPair> Options,
    int Answered,
    int Total);

/// <summary>
/// Життєвий цикл однієї сесії: створення черги питань, прийом відповідей і підсумкова
/// оцінка за Leitner. Черга генерується наперед і лежить у БД, тому бот можна
/// перезапустити посеред квізу, а callback_data вміщає лише два id.
/// </summary>
public class TrainingSessionService
{
    public const int NewBatchRepeats = 2;
    public const int ReviewRepeats = 1;
    public const double PassThreshold = 0.8;
    public const int MinBatchSize = 1;
    public const int MaxBatchSize = WordSelectionService.MaxCandidates;

    private readonly ApplicationDbContext _dbContext;
    private readonly WordSelectionService _selection;
    private readonly Random _rng = Random.Shared;

    public TrainingSessionService(ApplicationDbContext dbContext, WordSelectionService selection)
    {
        _dbContext = dbContext;
        _selection = selection;
    }

    public async Task<TelegramUser> GetOrCreateUserAsync(long telegramUserId)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId);

        if (user != null)
        {
            return user;
        }

        user = new TelegramUser { TelegramUserId = telegramUserId };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return user;
    }

    public async Task<Training?> StartNewBatchAsync(
        long userId,
        long dictionaryId,
        DateTime nowUtc,
        IReadOnlyList<long>? chapterIds = null,
        int batchSize = WordSelectionService.NewBatchSize,
        IReadOnlyList<long>? wordPairIds = null)
    {
        batchSize = Math.Clamp(batchSize, MinBatchSize, MaxBatchSize);

        // Явні id — «що бачив у превью, те й тренуєш»; без них — той самий топ за частотою, що й у превью.
        var words = wordPairIds is { Count: > 0 }
            ? (await _selection.GetLearnableByIdsAsync(userId, dictionaryId, chapterIds, wordPairIds)).Take(batchSize).ToList()
            : await _selection.GetNewBatchAsync(userId, dictionaryId, batchSize, chapterIds);

        if (words.Count == 0)
        {
            return null;
        }

        // Дистрактори — з усієї книжки, а не лише з глави: варіанти природніші,
        // а маленька глава не лишає квіз без валідних дистракторів.
        var pool = await _selection.GetDistractorPoolAsync(dictionaryId, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, dictionaryId, TrainingMode.NewBatch, words, NewBatchRepeats, pool, DirectionPolicy.EnToUa, nowUtc);
    }

    public async Task<Training?> StartReviewAsync(long userId, DateTime nowUtc)
    {
        var words = await _selection.GetDueWordsAsync(userId, nowUtc, WordSelectionService.ReviewSessionSize);

        if (words.Count == 0)
        {
            return null;
        }

        var pool = await _selection.GetDistractorPoolAsync(null, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, null, TrainingMode.Review, words, ReviewRepeats, pool, DirectionPolicy.Random, nowUtc);
    }

    public async Task<Training?> StartRetryAsync(long userId, long previousTrainingId, DateTime nowUtc)
    {
        var previous = await _dbContext.Trainings.FirstOrDefaultAsync(t => t.Id == previousTrainingId);

        if (previous == null)
        {
            return null;
        }

        var failedIds = await _dbContext.TrainingQuestions
            .Where(q => q.TrainingId == previousTrainingId && q.IsCorrect == false)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();

        if (failedIds.Count == 0)
        {
            return null;
        }

        var words = await _dbContext.Words.Where(w => failedIds.Contains(w.Id)).ToListAsync();
        var pool = await _selection.GetDistractorPoolAsync(
            previous.DictionaryId, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, previous.DictionaryId, TrainingMode.NewBatch, words, NewBatchRepeats, pool, DirectionPolicy.EnToUa, nowUtc);
    }

    public Task<TrainingQuestion?> GetNextQuestionAsync(long trainingId) =>
        _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .Where(q => q.TrainingId == trainingId && q.IsCorrect == null)
            .OrderBy(q => q.Order)
            .FirstOrDefaultAsync();

    public Task<Training?> FindAsync(long trainingId, long userId) =>
        _dbContext.Trainings.FirstOrDefaultAsync(t => t.Id == trainingId && t.UserId == userId);

    /// <summary>Слова сесії для фази карток — по одному разу, за алфавітом.</summary>
    public async Task<IReadOnlyList<WordPair>> GetBatchWordsAsync(long trainingId)
    {
        var wordIds = await _dbContext.TrainingQuestions
            .Where(q => q.TrainingId == trainingId)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();

        return await _dbContext.Words
            .Where(w => wordIds.Contains(w.Id))
            .OrderBy(w => w.Word)
            .ToListAsync();
    }

    /// <summary>
    /// OptionIds — звичайний масив без зовнішнього ключа: слово могло зникнути після генерації
    /// черги. Зниклі id пропускаємо, а правильну відповідь підставляємо назад, якщо її не лишилось.
    /// </summary>
    public async Task<QuestionView> GetNextQuestionViewAsync(long trainingId)
    {
        var total = await _dbContext.TrainingQuestions.CountAsync(q => q.TrainingId == trainingId);
        var answered = await _dbContext.TrainingQuestions.CountAsync(q => q.TrainingId == trainingId && q.IsCorrect != null);

        var question = await GetNextQuestionAsync(trainingId);

        if (question == null)
        {
            return new QuestionView(null, [], answered, total);
        }

        var found = await _dbContext.Words
            .Where(w => question.OptionIds.Contains(w.Id))
            .ToListAsync();

        var options = question.OptionIds
            .Select(id => found.FirstOrDefault(w => w.Id == id))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();

        if (options.All(w => w.Id != question.WordPairId))
        {
            options.Insert(0, question.WordPair);
        }

        return new QuestionView(question, options, answered, total);
    }

    public async Task<AnswerOutcome?> AnswerAsync(long questionId, long pickedWordPairId, DateTime nowUtc)
    {
        var question = await _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        // Питання може бути відсутнім (наприклад, слово видалили) або вже відповіданим —
        // у Телеграмі стара клавіатура лишається натискабельною, тому обидва випадки — no-op.
        if (question == null || question.IsCorrect != null)
        {
            return null;
        }

        question.PickedWordPairId = pickedWordPairId;
        question.IsCorrect = pickedWordPairId == question.WordPairId;
        question.AnsweredAt = nowUtc;

        await _dbContext.SaveChangesAsync();

        return new AnswerOutcome(question.IsCorrect.Value, question.WordPair);
    }

    public async Task<WordPair?> MarkKnownAsync(long questionId, DateTime nowUtc)
    {
        var question = await _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null)
        {
            return null;
        }

        var word = question.WordPair;
        var userId = question.UserId;
        var wordPairId = question.WordPairId;

        // Слово потрапляє в тренування лише з полиці «не знаю», тож перекласти його
        // на «знаю» — це саме перекласти, а не покласти вдруге: полиці взаємовиключні
        // (той самий інваріант, що й у WordSortingService.MarkAsync).
        var unknown = await _dbContext.UnknownWords
            .FirstOrDefaultAsync(u => u.UserId == userId && u.WordPairId == wordPairId);

        if (unknown != null)
        {
            _dbContext.UnknownWords.Remove(unknown);
        }

        var excluded = await _dbContext.ExcludedWords
            .FirstOrDefaultAsync(e => e.UserId == userId && e.WordPairId == wordPairId);

        if (excluded != null)
        {
            _dbContext.ExcludedWords.Remove(excluded);
        }

        if (!await _dbContext.KnownWords.AnyAsync(k => k.UserId == userId && k.WordPairId == wordPairId))
        {
            _dbContext.KnownWords.Add(new KnownWord { UserId = userId, WordPairId = wordPairId, CreatedAt = nowUtc });
        }

        var progress = await _dbContext.WordProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.WordPairId == wordPairId);

        if (progress == null)
        {
            // У новому батчі рядка прогресу для слова ще немає — його треба саме створити.
            progress = new WordProgress { UserId = userId, WordPairId = wordPairId, Box = LeitnerScheduler.MaxBox };
            _dbContext.WordProgresses.Add(progress);
        }

        progress.IsLearned = true;
        progress.DueAt = null;
        progress.LastSeenAt = nowUtc;

        // Слово більше не бере участі в оцінці Leitner цієї сесії — знімаємо геть усі його
        // питання, а не лише невідповідані, інакше FinishAsync побачить вже відповідане
        // питання й переоцінить щойно закріплене слово.
        var pending = await _dbContext.TrainingQuestions
            .Where(q => q.TrainingId == question.TrainingId && q.WordPairId == wordPairId)
            .ToListAsync();

        _dbContext.TrainingQuestions.RemoveRange(pending);
        await _dbContext.SaveChangesAsync();

        return word;
    }

    public async Task<string?> DeleteWordAsync(long questionId)
    {
        var question = await _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .FirstOrDefaultAsync(q => q.Id == questionId);

        if (question == null)
        {
            return null;
        }

        var word = question.WordPair;

        // Каскад налаштований лише на DictionaryWords, решту зносимо явно.
        _dbContext.KnownWords.RemoveRange(_dbContext.KnownWords.Where(k => k.WordPairId == word.Id));
        _dbContext.UnknownWords.RemoveRange(_dbContext.UnknownWords.Where(u => u.WordPairId == word.Id));
        _dbContext.WordProgresses.RemoveRange(_dbContext.WordProgresses.Where(p => p.WordPairId == word.Id));
        _dbContext.TrainingQuestions.RemoveRange(_dbContext.TrainingQuestions.Where(q => q.WordPairId == word.Id));
        _dbContext.Words.Remove(word);

        await _dbContext.SaveChangesAsync();

        return word.Word;
    }

    public async Task<TrainingSummary> FinishAsync(long trainingId, DateTime nowUtc)
    {
        var training = await _dbContext.Trainings.FirstAsync(t => t.Id == trainingId);

        var answered = await _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .Where(q => q.TrainingId == trainingId && q.IsCorrect != null)
            .ToListAsync();

        // Сесію можна довести до підсумку повторно: у Телеграмі стара клавіатура лишається
        // натискабельною, тож подвійний клік по останній відповіді знову веде хендлер сюди.
        // Удруге лише перечитуємо збережений стан, не оцінюючи наново.
        var alreadyFinished = training.FinishedAt != null;

        var results = new List<WordResult>();

        foreach (var group in answered.GroupBy(q => q.WordPairId))
        {
            var total = group.Count();
            var correct = group.Count(q => q.IsCorrect == true);
            var word = group.First().WordPair;

            var progress = await _dbContext.WordProgresses
                .FirstOrDefaultAsync(p => p.UserId == training.UserId && p.WordPairId == group.Key);

            if (alreadyFinished)
            {
                if (progress != null)
                {
                    results.Add(new WordResult(
                        word.Word, word.Translation, correct, total,
                        progress.Box, progress.DueAt, progress.IsLearned));
                }

                continue;
            }

            // Дві живі сесії можуть містити те саме слово: стара клавіатура лишається натискабельною,
            // а GetDueWordsAsync щоразу віддає той самий набір прострочених слів. Оцінюємо слово рівно
            // один раз — якщо його вже закріплено або вже оцінено сесією, що стартувала пізніше за цю.
            if (progress != null && (progress.IsLearned || progress.LastSeenAt > training.CreatedAt))
            {
                results.Add(new WordResult(
                    word.Word, word.Translation, correct, total,
                    progress.Box, progress.DueAt, progress.IsLearned));

                continue;
            }

            if (progress == null)
            {
                progress = new WordProgress
                {
                    UserId = training.UserId,
                    WordPairId = group.Key,
                    Box = LeitnerScheduler.MinBox
                };

                _dbContext.WordProgresses.Add(progress);
            }

            // Оцінка на агрегаті сесії, а не після кожної відповіді.
            var outcome = LeitnerScheduler.Grade(progress.Box, correct == total, nowUtc);

            progress.Box = outcome.Box;
            progress.DueAt = outcome.DueAt;
            progress.IsLearned = outcome.IsLearned;
            progress.CorrectCount += correct;
            progress.WrongCount += total - correct;
            progress.LastSeenAt = nowUtc;

            results.Add(new WordResult(
                word.Word, word.Translation, correct, total, outcome.Box, outcome.DueAt, outcome.IsLearned));
        }

        if (!alreadyFinished)
        {
            training.FinishedAt = nowUtc;
            await _dbContext.SaveChangesAsync();
        }

        var totalAnswers = answered.Count;
        var correctAnswers = answered.Count(q => q.IsCorrect == true);

        // Знаменник — фактично відповідані питання: слова, зняті кнопками,
        // зменшують і чисельник, і знаменник.
        var ratio = totalAnswers == 0 ? 0d : (double)correctAnswers / totalAnswers;

        return new TrainingSummary(correctAnswers, totalAnswers, ratio, totalAnswers > 0 && ratio >= PassThreshold, results);
    }

    public async Task<TrainingStats> GetStatsAsync(long userId, DateTime nowUtc)
    {
        var boxes = await _dbContext.WordProgresses
            .Where(p => p.UserId == userId && !p.IsLearned)
            .GroupBy(p => p.Box)
            .Select(g => new { Box = g.Key, Count = g.Count() })
            .ToListAsync();

        var boxCounts = Enumerable.Range(LeitnerScheduler.MinBox, LeitnerScheduler.MaxBox - LeitnerScheduler.MinBox + 1)
            .Select(box => boxes.FirstOrDefault(b => b.Box == box)?.Count ?? 0)
            .ToList();

        var learned = await _dbContext.WordProgresses.CountAsync(p => p.UserId == userId && p.IsLearned);
        var known = await _dbContext.KnownWords.CountAsync(k => k.UserId == userId);
        var due = await _selection.CountDueAsync(userId, nowUtc);

        var correct = await _dbContext.WordProgresses.Where(p => p.UserId == userId).SumAsync(p => p.CorrectCount);
        var wrong = await _dbContext.WordProgresses.Where(p => p.UserId == userId).SumAsync(p => p.WrongCount);

        return new TrainingStats(boxCounts, learned, known, due, correct, wrong);
    }

    private async Task<Training> CreateTrainingAsync(
        long userId,
        long? dictionaryId,
        TrainingMode mode,
        IReadOnlyList<WordPair> words,
        int repeats,
        IReadOnlyList<WordPair> distractorPool,
        DirectionPolicy policy,
        DateTime nowUtc)
    {
        var planned = QuestionQueueBuilder.Build(words, repeats, distractorPool, policy, _rng);

        var training = new Training
        {
            CreatedAt = nowUtc,
            Mode = mode,
            UserId = userId,
            DictionaryId = dictionaryId
        };

        _dbContext.Trainings.Add(training);
        await _dbContext.SaveChangesAsync();

        for (var i = 0; i < planned.Count; i++)
        {
            _dbContext.TrainingQuestions.Add(new TrainingQuestion
            {
                CreatedAt = nowUtc,
                TrainingId = training.Id,
                UserId = userId,
                WordPairId = planned[i].WordPairId,
                Order = i,
                Direction = planned[i].Direction,
                OptionIds = planned[i].OptionIds.ToList()
            });
        }

        await _dbContext.SaveChangesAsync();
        return training;
    }
}
