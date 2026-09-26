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
    IReadOnlyList<int> BoxCounts,   // index 0 = box 1, length = LeitnerScheduler.MaxBox
    int Learned,
    int Known,                      // sorting shelves, global across books: "know" / "don't know" / excluded
    int Unknown,
    int Excluded,
    int Due,
    int Correct,
    int Wrong);

/// <summary>
/// The next question for the UI: options in OptionIds order plus the session counters.
/// Question == null means the queue is exhausted — the counters are final at that point.
/// </summary>
public sealed record QuestionView(
    TrainingQuestion? Question,
    IReadOnlyList<WordPair> Options,
    int Answered,
    int Total);

/// <summary>
/// The life cycle of one session: building the question queue, taking answers and the final
/// Leitner grading. The queue is generated up front and lives in the DB, so the bot could be
/// restarted mid-quiz and callback_data only had to carry two ids.
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

        // Explicit ids — "you train what the preview showed"; without them, the same frequency top the preview uses.
        var words = wordPairIds is { Count: > 0 }
            ? (await _selection.GetLearnableByIdsAsync(userId, dictionaryId, chapterIds, wordPairIds)).Take(batchSize).ToList()
            : await _selection.GetNewBatchAsync(userId, dictionaryId, batchSize, chapterIds);

        if (words.Count == 0)
        {
            return null;
        }

        // Distractors come from the whole book, not just the chapter: the options read more
        // naturally, and a small chapter does not leave the quiz without valid distractors.
        var pool = await _selection.GetDistractorPoolAsync(userId, dictionaryId, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, dictionaryId, ScopeChapterOf(chapterIds), TrainingMode.NewBatch, words, NewBatchRepeats, pool,
            DirectionPolicy.EnToUa, nowUtc);
    }

    /// <summary>
    /// Without a scope: everything due, across all books. With one: only that chapter's (or
    /// book's) due words, and the session remembers the book — distractors then come from
    /// it too, as they do for a new batch, instead of from every word there is.
    /// </summary>
    public async Task<Training?> StartReviewAsync(
        long userId, DateTime nowUtc, long? dictionaryId = null, IReadOnlyList<long>? chapterIds = null)
    {
        var words = await _selection.GetDueWordsAsync(
            userId, nowUtc, WordSelectionService.ReviewSessionSize, dictionaryId, chapterIds);

        if (words.Count == 0)
        {
            return null;
        }

        var pool = await _selection.GetDistractorPoolAsync(userId, dictionaryId, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, dictionaryId, ScopeChapterOf(chapterIds), TrainingMode.Review, words, ReviewRepeats, pool,
            DirectionPolicy.Random, nowUtc);
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
            userId, previous.DictionaryId, WordSelectionService.DistractorPoolSize, _rng);

        return await CreateTrainingAsync(
            userId, previous.DictionaryId, previous.ChapterId, TrainingMode.NewBatch, words, NewBatchRepeats, pool,
            DirectionPolicy.EnToUa, nowUtc);
    }

    /// <summary>
    /// The one chapter a session was scoped to, for <see cref="Training.ChapterId"/>. Several
    /// chapters at once have no single scope to reopen, so they read as the whole book.
    /// </summary>
    private static long? ScopeChapterOf(IReadOnlyList<long>? chapterIds) =>
        chapterIds is { Count: 1 } ? chapterIds[0] : null;

    public Task<TrainingQuestion?> GetNextQuestionAsync(long trainingId) =>
        _dbContext.TrainingQuestions
            .Include(q => q.WordPair)
            .Where(q => q.TrainingId == trainingId && q.IsCorrect == null)
            .OrderBy(q => q.Order)
            .FirstOrDefaultAsync();

    public Task<Training?> FindAsync(long trainingId, long userId) =>
        _dbContext.Trainings.FirstOrDefaultAsync(t => t.Id == trainingId && t.UserId == userId);

    /// <summary>The session's words for the card phase — each once, alphabetically.</summary>
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
    /// OptionIds is a plain array with no foreign key: a word may have vanished after the queue
    /// was generated. Missing ids are skipped, and the correct answer is put back if it is gone.
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

        // The question may be missing (say, the word was deleted) or already answered — in
        // Telegram the old keyboard stays clickable, so both cases are a no-op.
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

        // A word only enters training from the "don't know" shelf, so moving it to "know"
        // is exactly a move, not a second placement: the shelves are mutually exclusive
        // (the same invariant WordSortingService.MarkAsync keeps).
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
            // In a new batch the word has no progress row yet — it has to be created here.
            progress = new WordProgress { UserId = userId, WordPairId = wordPairId, Box = LeitnerScheduler.MaxBox };
            _dbContext.WordProgresses.Add(progress);
        }

        progress.IsLearned = true;
        progress.DueAt = null;
        progress.LastSeenAt = nowUtc;

        // The word no longer takes part in this session's Leitner grading — drop all of its
        // questions, not just the unanswered ones, otherwise FinishAsync would see an answered
        // question and re-grade a word that was just fixed.
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

        // Cascade is configured only on DictionaryWords; the rest is deleted explicitly.
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

        // A session can be brought to its summary twice: in Telegram the old keyboard stays
        // clickable, so a double click on the last answer leads the handler here again.
        // The second time only re-reads the stored state without grading anew.
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

            // Two live sessions can hold the same word: the old keyboard stays clickable, and
            // GetDueWordsAsync returns the same set of due words every time. Grade the word exactly
            // once — skip it if it is already fixed or already graded by a session started after this one.
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

            // Grading on the session's aggregate, not after every answer.
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

        // The denominator is the questions actually answered: words removed via the
        // buttons shrink both the numerator and the denominator.
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
        var unknown = await _dbContext.UnknownWords.CountAsync(u => u.UserId == userId);
        var excluded = await _dbContext.ExcludedWords.CountAsync(e => e.UserId == userId);
        var due = await _selection.CountDueAsync(userId, nowUtc);

        var correct = await _dbContext.WordProgresses.Where(p => p.UserId == userId).SumAsync(p => p.CorrectCount);
        var wrong = await _dbContext.WordProgresses.Where(p => p.UserId == userId).SumAsync(p => p.WrongCount);

        return new TrainingStats(boxCounts, learned, known, unknown, excluded, due, correct, wrong);
    }

    private async Task<Training> CreateTrainingAsync(
        long userId,
        long? dictionaryId,
        long? chapterId,
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
            DictionaryId = dictionaryId,
            ChapterId = chapterId
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
