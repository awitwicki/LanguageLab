using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using LanguageLab.TgBot.Services;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class WordSelectionServiceTests
{
    private const long UserId = 1;
    private const long DictionaryId = 1;
    private static readonly DateTime Now = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WordPair Word(long id, string word, string translation) =>
        new() { Id = id, Word = word, Translation = translation };

    /// <summary>
    /// Один граф, що містить по одному представнику кожного правила виключення.
    /// Придатні до навчання тільки слова 1 і 2.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();

        var silo = new LanguageLab.Domain.Entities.Dictionary { Id = 1, Name = "silo1", WordsCount = 6 };
        var other = new LanguageLab.Domain.Entities.Dictionary { Id = 2, Name = "other", WordsCount = 1 };

        var learnableOne = Word(1, "abide", "дотримуватися");
        var learnableTwo = Word(2, "abdomen", "черевна порожнина");
        var alreadyKnown = Word(3, "able", "здатний");
        var alreadyInProgress = Word(4, "aback", "зненацька");
        var orphanWithoutTranslation = Word(5, "orphan", "");
        var fromAnotherDictionary = Word(6, "outside", "ззовні");
        var neverSorted = Word(7, "unsorted", "невідсортоване");

        silo.Words = [learnableOne, learnableTwo, alreadyKnown, alreadyInProgress, orphanWithoutTranslation, neverSorted];
        other.Words = [fromAnotherDictionary];

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.AddRange(silo, other);

        // Усе, крім слова 7, юзер позначив як «хочу вчити».
        for (long wordPairId = 1; wordPairId <= 6; wordPairId++)
        {
            db.UnknownWords.Add(new UnknownWord { Id = wordPairId, UserId = UserId, WordPairId = wordPairId });
        }

        db.KnownWords.Add(new KnownWord { Id = 1, UserId = UserId, WordPairId = alreadyKnown.Id });

        db.WordProgresses.Add(new WordProgress
        {
            Id = 1,
            UserId = UserId,
            WordPairId = alreadyInProgress.Id,
            Box = 2,
            DueAt = Now.AddDays(3),
            LastSeenAt = Now
        });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// count слів, усі однаково придатні до навчання (у словнику, з перекладом,
    /// «хочу вчити», не «знаю», без WordProgress) — щоб перевірити, що порядок
    /// повернутого батчу справді перемішаний, а не є порядком бази даних.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeManyLearnableAsync(int count)
    {
        var db = NewContext();

        var dictionary = new LanguageLab.Domain.Entities.Dictionary { Id = DictionaryId, Name = "silo1", WordsCount = count };
        var words = Enumerable.Range(1, count)
            .Select(i => Word(i, $"word{i}", $"переклад{i}"))
            .ToList();
        dictionary.Words = words;

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.Add(dictionary);

        foreach (var word in words)
        {
            db.UnknownWords.Add(new UnknownWord { Id = word.Id, UserId = UserId, WordPairId = word.Id });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task NewBatch_ReturnsOnlyLearnableWords()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSelectionService(db);

        var batch = await service.GetNewBatchAsync(UserId, DictionaryId, size: 5, new Random(1));

        Assert.Equal(new[] { 1L, 2L }, batch.Select(w => w.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task NewBatch_NeverExceedsRequestedSize()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSelectionService(db);

        var batch = await service.GetNewBatchAsync(UserId, DictionaryId, size: 1, new Random(1));

        Assert.Single(batch);
    }

    [Fact]
    public async Task NewBatch_IsEmptyWhenNothingLeftToLearn()
    {
        await using var db = await ArrangeAsync();
        db.WordProgresses.AddRange(
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 1, Box = 1, DueAt = Now, LastSeenAt = Now },
            new WordProgress { Id = 3, UserId = UserId, WordPairId = 2, Box = 1, DueAt = Now, LastSeenAt = Now });
        await db.SaveChangesAsync();

        var batch = await new WordSelectionService(db).GetNewBatchAsync(UserId, DictionaryId, size: 5, new Random(1));

        Assert.Empty(batch);
    }

    [Fact]
    public async Task NewBatch_ReturnsWordsInTheShuffledOrder_NotDatabaseOrder()
    {
        await using var db = await ArrangeManyLearnableAsync(count: 20);

        var batch = await new WordSelectionService(db).GetNewBatchAsync(UserId, DictionaryId, size: 20, new Random(7));
        var ids = batch.Select(w => w.Id).ToList();

        var expectedIds = Enumerable.Range(1, 20).Select(i => (long)i).ToList();
        Assert.Equal(expectedIds, ids.OrderBy(id => id));
        Assert.NotEqual(expectedIds, ids);
    }

    [Fact]
    public async Task CountLearnable_MatchesBatchSelectionRules()
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(2, await new WordSelectionService(db).CountLearnableAsync(UserId, DictionaryId));
    }

    [Fact]
    public async Task DueWords_ReturnOnlyOverdueUnlearnedWordsOrderedByDueDate()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Words.AddRange(
            Word(1, "one", "один"), Word(2, "two", "два"),
            Word(3, "three", "три"), Word(4, "four", "чотири"));

        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 1, DueAt = Now.AddDays(-1), LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 1, DueAt = Now.AddDays(-3), LastSeenAt = Now },
            new WordProgress { Id = 3, UserId = UserId, WordPairId = 3, Box = 1, DueAt = Now.AddDays(2), LastSeenAt = Now },
            new WordProgress { Id = 4, UserId = UserId, WordPairId = 4, Box = 5, DueAt = null, IsLearned = true, LastSeenAt = Now });
        await db.SaveChangesAsync();

        var due = await new WordSelectionService(db).GetDueWordsAsync(UserId, Now, size: 20);

        Assert.Equal(new[] { 2L, 1L }, due.Select(w => w.Id));
    }

    [Fact]
    public async Task CountDue_IgnoresFutureAndLearnedWords()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Words.AddRange(Word(1, "one", "один"), Word(2, "two", "два"), Word(3, "three", "три"));
        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 1, DueAt = Now.AddMinutes(-1), LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 1, DueAt = Now.AddDays(5), LastSeenAt = Now },
            new WordProgress { Id = 3, UserId = UserId, WordPairId = 3, Box = 5, DueAt = null, IsLearned = true, LastSeenAt = Now });
        await db.SaveChangesAsync();

        Assert.Equal(1, await new WordSelectionService(db).CountDueAsync(UserId, Now));
    }

    [Fact]
    public async Task DistractorPool_SkipsWordsWithoutTranslation()
    {
        await using var db = await ArrangeAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(DictionaryId, size: 60, new Random(1));

        Assert.DoesNotContain(pool, w => w.Translation.Length == 0);
        Assert.All(pool, w => Assert.NotEqual(6L, w.Id));
    }

    [Fact]
    public async Task DistractorPool_WithoutDictionary_SpansAllWords()
    {
        await using var db = await ArrangeAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(dictionaryId: null, size: 60, new Random(1));

        Assert.Contains(pool, w => w.Id == 6);
    }
}
