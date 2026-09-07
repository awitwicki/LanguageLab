using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class TrainingSessionServiceTests
{
    private const long UserId = 1;
    private const long DictionaryId = 1;
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>Словник із 12 слів, усі позначені юзером як «хочу вчити».</summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();

        var words = Enumerable.Range(1, 12)
            .Select(i => new WordPair { Id = i, Word = $"word{i}", Translation = $"переклад{i}" })
            .ToList();

        var dictionary = new LanguageLab.Domain.Entities.Dictionary
        {
            Id = DictionaryId,
            Name = "silo1",
            WordsCount = words.Count,
            Words = words
        };

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.Add(dictionary);

        foreach (var word in words)
        {
            db.UnknownWords.Add(new UnknownWord { Id = word.Id, UserId = UserId, WordPairId = word.Id });
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static TrainingSessionService Service(ApplicationDbContext db) =>
        new(db, new WordSelectionService(db));

    private static async Task AnswerEverythingAsync(
        TrainingSessionService service, long trainingId, params long[] wordIdsToFail)
    {
        var failed = new HashSet<long>();

        while (await service.GetNextQuestionAsync(trainingId) is { } question)
        {
            // Кожне «провальне» слово помиляємо рівно один раз — цього досить,
            // щоб воно не порахувалося як allCorrect.
            var shouldFail = wordIdsToFail.Contains(question.WordPairId) && failed.Add(question.WordPairId);

            var picked = shouldFail
                ? question.OptionIds.First(id => id != question.WordPairId)
                : question.WordPairId;

            await service.AnswerAsync(question.Id, picked, Now);
        }
    }

    [Fact]
    public async Task StartNewBatch_CreatesTenQuestionsForFiveWords()
    {
        await using var db = await ArrangeAsync();

        var training = await Service(db).StartNewBatchAsync(UserId, DictionaryId, Now);

        Assert.NotNull(training);
        Assert.Equal(TrainingMode.NewBatch, training.Mode);
        Assert.Equal(DictionaryId, training.DictionaryId);

        var questions = await db.TrainingQuestions.Where(q => q.TrainingId == training.Id).ToListAsync();
        Assert.Equal(10, questions.Count);
        Assert.Equal(5, questions.Select(q => q.WordPairId).Distinct().Count());
        Assert.All(questions, q => Assert.Equal(QuestionDirection.EnToUa, q.Direction));
    }

    [Fact]
    public async Task StartNewBatch_ReturnsNullWhenNothingLeftToLearn()
    {
        await using var db = await ArrangeAsync();
        db.UnknownWords.RemoveRange(db.UnknownWords);
        await db.SaveChangesAsync();

        Assert.Null(await Service(db).StartNewBatchAsync(UserId, DictionaryId, Now));
    }

    [Fact]
    public async Task Answer_RecordsResultAndIgnoresSecondClickOnSameQuestion()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var question = await service.GetNextQuestionAsync(training!.Id);

        var first = await service.AnswerAsync(question!.Id, question.WordPairId, Now);
        var second = await service.AnswerAsync(question.Id, question.WordPairId, Now);

        Assert.NotNull(first);
        Assert.True(first.IsCorrect);
        Assert.Null(second);
    }

    [Fact]
    public async Task Answer_MarksWrongPickAsIncorrect()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var question = await service.GetNextQuestionAsync(training!.Id);
        var wrongPick = question!.OptionIds.First(id => id != question.WordPairId);

        var outcome = await service.AnswerAsync(question.Id, wrongPick, Now);

        Assert.False(outcome!.IsCorrect);
    }

    [Fact]
    public async Task MarkKnown_LearnsWordAndDropsItsRemainingQuestions()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var question = await service.GetNextQuestionAsync(training!.Id);
        var wordPairId = question!.WordPairId;

        await service.MarkKnownAsync(question.Id, Now);

        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == UserId && k.WordPairId == wordPairId));

        var progress = await db.WordProgresses.SingleAsync(p => p.UserId == UserId && p.WordPairId == wordPairId);
        Assert.True(progress.IsLearned);
        Assert.Null(progress.DueAt);

        Assert.False(await db.TrainingQuestions.AnyAsync(q => q.TrainingId == training.Id && q.WordPairId == wordPairId));
    }

    [Fact]
    public async Task MarkKnown_MovesTheWordOffTheUnknownShelfInsteadOfDuplicatingIt()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var question = await service.GetNextQuestionAsync(training!.Id);
        var wordPairId = question!.WordPairId;

        // У тренування слово потрапляє тільки з полиці «не знаю» — тобто рядок там є.
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == UserId && u.WordPairId == wordPairId));

        await service.MarkKnownAsync(question.Id, Now);

        var known = await db.KnownWords.SingleAsync(k => k.UserId == UserId && k.WordPairId == wordPairId);
        Assert.Equal(Now, known.CreatedAt);
        Assert.False(await db.UnknownWords.AnyAsync(u => u.UserId == UserId && u.WordPairId == wordPairId));
    }

    [Fact]
    public async Task DeleteWord_RemovesWordAndEverythingPointingAtIt()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var question = await service.GetNextQuestionAsync(training!.Id);
        var wordPairId = question!.WordPairId;

        var deleted = await service.DeleteWordAsync(question.Id);

        Assert.NotNull(deleted);
        Assert.False(await db.Words.AnyAsync(w => w.Id == wordPairId));
        Assert.False(await db.UnknownWords.AnyAsync(u => u.WordPairId == wordPairId));
        Assert.False(await db.TrainingQuestions.AnyAsync(q => q.WordPairId == wordPairId));
    }

    [Fact]
    public async Task Finish_PromotesCleanWordsAndDemotesTheOneWithAMistake()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var failedWordId = (await db.TrainingQuestions
            .Where(q => q.TrainingId == training!.Id)
            .Select(q => q.WordPairId)
            .FirstAsync());

        await AnswerEverythingAsync(service, training!.Id, failedWordId);

        var summary = await service.FinishAsync(training.Id, Now);

        Assert.Equal(10, summary.Total);
        Assert.Equal(9, summary.Correct);
        Assert.True(summary.Passed);

        var promoted = await db.WordProgresses
            .Where(p => p.UserId == UserId && p.WordPairId != failedWordId)
            .ToListAsync();

        Assert.Equal(4, promoted.Count);
        Assert.All(promoted, p =>
        {
            Assert.Equal(2, p.Box);
            Assert.Equal(Now.AddDays(3), p.DueAt);
        });

        var demoted = await db.WordProgresses.SingleAsync(p => p.UserId == UserId && p.WordPairId == failedWordId);
        Assert.Equal(1, demoted.Box);
        Assert.Equal(Now.AddDays(1), demoted.DueAt);
        Assert.Equal(1, demoted.WrongCount);
        Assert.Equal(1, demoted.CorrectCount);
    }

    [Fact]
    public async Task Finish_BelowThreshold_IsNotPassed()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var wordIds = await db.TrainingQuestions
            .Where(q => q.TrainingId == training!.Id)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();

        await AnswerEverythingAsync(service, training!.Id, wordIds.Take(3).ToArray());

        var summary = await service.FinishAsync(training.Id, Now);

        Assert.Equal(7, summary.Correct);
        Assert.Equal(10, summary.Total);
        Assert.False(summary.Passed);
    }

    [Fact]
    public async Task Finish_IgnoresWordsSkippedByKnownButton()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var first = await service.GetNextQuestionAsync(training!.Id);
        await service.MarkKnownAsync(first!.Id, Now);

        await AnswerEverythingAsync(service, training.Id);

        var summary = await service.FinishAsync(training.Id, Now);

        Assert.Equal(8, summary.Total);
        Assert.Equal(8, summary.Correct);
        Assert.Equal(4, summary.Words.Count);
        Assert.DoesNotContain(summary.Words, w => w.Word == first.WordPair.Word);
    }

    [Fact]
    public async Task Review_UsesDueWordsAndAsksEachOnce()
    {
        await using var db = await ArrangeAsync();
        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 2, DueAt = Now.AddDays(-1), LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 3, DueAt = Now.AddDays(-2), LastSeenAt = Now });
        await db.SaveChangesAsync();

        var training = await Service(db).StartReviewAsync(UserId, Now);

        Assert.NotNull(training);
        Assert.Equal(TrainingMode.Review, training.Mode);
        Assert.Null(training.DictionaryId);

        var questions = await db.TrainingQuestions.Where(q => q.TrainingId == training.Id).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.Equal(new[] { 1L, 2L }, questions.Select(q => q.WordPairId).OrderBy(id => id));
    }

    [Fact]
    public async Task Retry_RebuildsSessionFromFailedWordsOnly()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var wordIds = await db.TrainingQuestions
            .Where(q => q.TrainingId == training!.Id)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();
        var failed = wordIds.Take(2).ToArray();

        await AnswerEverythingAsync(service, training!.Id, failed);
        await service.FinishAsync(training.Id, Now);

        var retry = await service.StartRetryAsync(UserId, training.Id, Now);

        Assert.NotNull(retry);
        var retryQuestions = await db.TrainingQuestions.Where(q => q.TrainingId == retry.Id).ToListAsync();
        Assert.Equal(4, retryQuestions.Count);
        Assert.Equal(failed.OrderBy(id => id), retryQuestions.Select(q => q.WordPairId).Distinct().OrderBy(id => id));
    }

    [Fact]
    public async Task Finish_IsIdempotent_AndDoesNotRegradeOnASecondCall()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);

        await AnswerEverythingAsync(service, training!.Id);
        await service.FinishAsync(training.Id, Now);

        var before = await db.WordProgresses
            .Where(p => p.UserId == UserId)
            .Select(p => new { p.WordPairId, p.Box, p.DueAt, p.CorrectCount, p.WrongCount })
            .ToListAsync();
        Assert.Equal(5, before.Count);

        // Пізніший nowUtc, щоб виявити регресію: якщо захист не спрацює, DueAt зсунеться,
        // а тест з тим самим часом міг би випадково лишитися зеленим.
        await service.FinishAsync(training.Id, Now.AddDays(10));

        var after = await db.WordProgresses
            .Where(p => p.UserId == UserId)
            .Select(p => new { p.WordPairId, p.Box, p.DueAt, p.CorrectCount, p.WrongCount })
            .ToListAsync();

        Assert.Equal(before.Count, after.Count);

        foreach (var b in before)
        {
            var a = after.Single(x => x.WordPairId == b.WordPairId);
            Assert.Equal(b.Box, a.Box);
            Assert.Equal(b.DueAt, a.DueAt);
            Assert.Equal(b.CorrectCount, a.CorrectCount);
            Assert.Equal(b.WrongCount, a.WrongCount);
        }
    }

    [Fact]
    public async Task MarkKnown_AfterAWrongAnswerOnTheSameWord_KeepsTheWordLearned()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);

        var first = await service.GetNextQuestionAsync(training!.Id);
        var wordPairId = first!.WordPairId;
        var wrongPick = first.OptionIds.First(id => id != wordPairId);
        await service.AnswerAsync(first.Id, wrongPick, Now);

        // Черга навмисно не ставить те саме слово двічі підряд — тому беремо саме друге
        // питання цього слова за Order, а не покладаємося на GetNextQuestionAsync.
        var questionsForWord = await db.TrainingQuestions
            .Where(q => q.TrainingId == training.Id && q.WordPairId == wordPairId)
            .OrderBy(q => q.Order)
            .ToListAsync();
        var secondQuestionForWord = questionsForWord[1];

        await service.MarkKnownAsync(secondQuestionForWord.Id, Now);

        await AnswerEverythingAsync(service, training.Id);

        var summary = await service.FinishAsync(training.Id, Now);

        var progress = await db.WordProgresses.SingleAsync(p => p.UserId == UserId && p.WordPairId == wordPairId);
        Assert.True(progress.IsLearned);
        Assert.Null(progress.DueAt);

        var word = await db.Words.SingleAsync(w => w.Id == wordPairId);
        Assert.DoesNotContain(summary.Words, w => w.Word == word.Word);
        Assert.Equal(8, summary.Total);
    }

    [Fact]
    public async Task GetStats_AggregatesBoxHistogramLearnedKnownDueAndAnswerCounts()
    {
        await using var db = await ArrangeAsync();

        // Гістограма навмисно нерівномірна (2/1/1/0/1), щоб зсув індексу на одну позицію
        // (box N потрапив би в комірку N замість N-1) провалив тест.
        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 1, CorrectCount = 2, WrongCount = 1, LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 1, CorrectCount = 1, WrongCount = 0, LastSeenAt = Now },
            new WordProgress { Id = 3, UserId = UserId, WordPairId = 3, Box = 2, CorrectCount = 3, WrongCount = 1, DueAt = Now.AddDays(-1), LastSeenAt = Now },
            new WordProgress { Id = 4, UserId = UserId, WordPairId = 4, Box = 3, CorrectCount = 0, WrongCount = 2, DueAt = Now.AddDays(5), LastSeenAt = Now },
            new WordProgress { Id = 5, UserId = UserId, WordPairId = 5, Box = 5, CorrectCount = 4, WrongCount = 0, LastSeenAt = Now },
            // Вивчене слово: не входить у гістограму боксів (IsLearned == true), лише в Learned.
            new WordProgress { Id = 6, UserId = UserId, WordPairId = 6, Box = 5, IsLearned = true, CorrectCount = 5, WrongCount = 1, LastSeenAt = Now });

        db.KnownWords.AddRange(
            new KnownWord { Id = 1, UserId = UserId, WordPairId = 7 },
            new KnownWord { Id = 2, UserId = UserId, WordPairId = 8 });

        await db.SaveChangesAsync();

        var stats = await Service(db).GetStatsAsync(UserId, Now);

        Assert.Equal(LeitnerScheduler.MaxBox, stats.BoxCounts.Count);
        Assert.Equal(new[] { 2, 1, 1, 0, 1 }, stats.BoxCounts);
        Assert.Equal(1, stats.Learned);
        Assert.Equal(2, stats.Known);
        Assert.Equal(1, stats.Due);
        Assert.Equal(15, stats.Correct);
        Assert.Equal(5, stats.Wrong);
    }

    [Fact]
    public async Task Review_RunTwiceOverTheSameDueWords_GradesEachWordOnlyOnce()
    {
        await using var db = await ArrangeAsync();
        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 2, CorrectCount = 1, DueAt = Now.AddDays(-1), LastSeenAt = Now.AddDays(-3) },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 3, CorrectCount = 2, DueAt = Now.AddDays(-2), LastSeenAt = Now.AddDays(-3) });
        await db.SaveChangesAsync();

        var service = Service(db);

        // Обидві сесії стартують до того, як хоч одна завершиться — тому обидві бачать
        // однаковий набір прострочених слів.
        var sessionA = await service.StartReviewAsync(UserId, Now.AddMinutes(1));
        var sessionB = await service.StartReviewAsync(UserId, Now.AddMinutes(2));

        Assert.NotNull(sessionA);
        Assert.NotNull(sessionB);

        var wordsA = await db.TrainingQuestions.Where(q => q.TrainingId == sessionA!.Id)
            .Select(q => q.WordPairId).Distinct().ToListAsync();
        var wordsB = await db.TrainingQuestions.Where(q => q.TrainingId == sessionB!.Id)
            .Select(q => q.WordPairId).Distinct().ToListAsync();
        Assert.Equal(new[] { 1L, 2L }, wordsA.OrderBy(id => id));
        Assert.Equal(new[] { 1L, 2L }, wordsB.OrderBy(id => id));

        await AnswerEverythingAsync(service, sessionA!.Id);
        await service.FinishAsync(sessionA.Id, Now.AddMinutes(3));

        await AnswerEverythingAsync(service, sessionB!.Id);
        await service.FinishAsync(sessionB.Id, Now.AddMinutes(4));

        var progress1 = await db.WordProgresses.SingleAsync(p => p.WordPairId == 1);
        var progress2 = await db.WordProgresses.SingleAsync(p => p.WordPairId == 2);

        // Кожне слово мало просунутися рівно на один бокс і отримати рівно один
        // додатковий правильний залік — а не подвоєно другою (стільки ж) сесією.
        Assert.Equal(3, progress1.Box);
        Assert.Equal(2, progress1.CorrectCount);
        Assert.Equal(4, progress2.Box);
        Assert.Equal(3, progress2.CorrectCount);
    }

    [Fact]
    public async Task MarkKnown_IsNotUndoneByAnotherLiveSessionGradingTheWordWrong()
    {
        await using var db = await ArrangeAsync();
        db.WordProgresses.Add(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 2, DueAt = Now.AddDays(-1), LastSeenAt = Now.AddDays(-3) });
        await db.SaveChangesAsync();

        var service = Service(db);

        var sessionA = await service.StartReviewAsync(UserId, Now);
        var sessionB = await service.StartReviewAsync(UserId, Now);

        Assert.NotNull(sessionA);
        Assert.NotNull(sessionB);

        var questionA = await service.GetNextQuestionAsync(sessionA!.Id);
        Assert.Equal(1, questionA!.WordPairId);
        await service.MarkKnownAsync(questionA.Id, Now.AddMinutes(1));

        var questionB = await service.GetNextQuestionAsync(sessionB!.Id);
        Assert.Equal(1, questionB!.WordPairId);
        var wrongPick = questionB.OptionIds.First(id => id != questionB.WordPairId);
        await service.AnswerAsync(questionB.Id, wrongPick, Now.AddMinutes(2));

        await service.FinishAsync(sessionB.Id, Now.AddMinutes(2));

        var progress = await db.WordProgresses.SingleAsync(p => p.WordPairId == 1);
        Assert.True(progress.IsLearned);
        Assert.Null(progress.DueAt);
    }

    [Fact]
    public async Task Retry_StillRegradesAfterTheGuard()
    {
        // Єдиний нерухомий Now скрізь — регресійний тест саме на межу `>` проти `>=`:
        // якщо охорону послабити до `>=`, LastSeenAt (== Now після першого FinishAsync)
        // помилково «затулить» CreatedAt повторної сесії (теж == Now), і бокси не зрушать.
        await using var db = await ArrangeAsync();
        var service = Service(db);
        var training = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var wordIds = await db.TrainingQuestions
            .Where(q => q.TrainingId == training!.Id)
            .Select(q => q.WordPairId)
            .Distinct()
            .ToListAsync();
        var failed = wordIds.Take(2).ToArray();

        await AnswerEverythingAsync(service, training!.Id, failed);
        await service.FinishAsync(training.Id, Now);

        var boxesBefore = await db.WordProgresses
            .Where(p => failed.Contains(p.WordPairId))
            .ToDictionaryAsync(p => p.WordPairId, p => p.Box);

        var retry = await service.StartRetryAsync(UserId, training.Id, Now);
        Assert.NotNull(retry);

        await AnswerEverythingAsync(service, retry!.Id);
        await service.FinishAsync(retry.Id, Now);

        var boxesAfter = await db.WordProgresses
            .Where(p => failed.Contains(p.WordPairId))
            .ToListAsync();

        Assert.All(boxesAfter, p => Assert.True(
            p.Box > boxesBefore[p.WordPairId],
            $"word {p.WordPairId} box did not move ({boxesBefore[p.WordPairId]} -> {p.Box})"));
    }

    [Fact]
    public async Task CorrectCount_AccumulatesAcrossTwoLegitimateSessions()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        var batch = await service.StartNewBatchAsync(UserId, DictionaryId, Now);
        var wordId = await db.TrainingQuestions
            .Where(q => q.TrainingId == batch!.Id)
            .Select(q => q.WordPairId)
            .FirstAsync();

        await AnswerEverythingAsync(service, batch!.Id);
        await service.FinishAsync(batch.Id, Now);

        var afterBatch = await db.WordProgresses.SingleAsync(p => p.WordPairId == wordId);
        Assert.Equal(TrainingSessionService.NewBatchRepeats, afterBatch.CorrectCount);
        Assert.Equal(0, afterBatch.WrongCount);

        var reviewNow = Now.AddDays(4);
        var review = await service.StartReviewAsync(UserId, reviewNow);
        Assert.NotNull(review);
        var reviewWords = await db.TrainingQuestions
            .Where(q => q.TrainingId == review!.Id)
            .Select(q => q.WordPairId)
            .ToListAsync();
        Assert.Contains(wordId, reviewWords);

        await AnswerEverythingAsync(service, review!.Id);
        await service.FinishAsync(review.Id, reviewNow);

        var afterReview = await db.WordProgresses.SingleAsync(p => p.WordPairId == wordId);

        // += має накопичувати через сесії, а не перезаписувати: якби FinishAsync писав
        // "=" замість "+=", тут було б знову NewBatchRepeats, а не сума обох сесій.
        Assert.Equal(TrainingSessionService.NewBatchRepeats + TrainingSessionService.ReviewRepeats, afterReview.CorrectCount);
        Assert.Equal(0, afterReview.WrongCount);
    }
}
