using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Application.Services;
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
        var excluded = Word(8, "solo", "соло");

        silo.Words = [learnableOne, learnableTwo, alreadyKnown, alreadyInProgress, orphanWithoutTranslation, neverSorted, excluded];
        other.Words = [fromAnotherDictionary];

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.AddRange(silo, other);

        // Усе, крім слова 7, юзер позначив як «хочу вчити».
        for (long wordPairId = 1; wordPairId <= 6; wordPairId++)
        {
            db.UnknownWords.Add(new UnknownWord { Id = wordPairId, UserId = UserId, WordPairId = wordPairId });
        }

        // Слово 8 юзер позначив і як «хочу вчити», і як виключене — виключення має перемогти.
        db.UnknownWords.Add(new UnknownWord { Id = 8, UserId = UserId, WordPairId = excluded.Id });
        db.ExcludedWords.Add(new ExcludedWord
        {
            Id = 1,
            UserId = UserId,
            WordPairId = excluded.Id,
            CreatedAt = Now
        });

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
    /// Книжка з двома главами. Глава 1: слова 1, 2, 3; глава 2: слова 4, 5, 6.
    /// Усі на полиці «хочу вчити», але слово 3 юзер уже знає, а слово 6 без перекладу —
    /// тож придатні до навчання: 1, 2 (глава 1) і 4, 5 (глава 2).
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeWithChaptersAsync()
    {
        var db = NewContext();

        var words = Enumerable.Range(1, 6)
            .Select(i => Word(i, $"word{i}", i == 6 ? "" : $"переклад{i}"))
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

        db.Chapters.Add(new Chapter { Id = 1, DictionaryId = DictionaryId, Order = 0, Title = "One", WordsCount = 3 });
        db.Chapters.Add(new Chapter { Id = 2, DictionaryId = DictionaryId, Order = 1, Title = "Two", WordsCount = 3 });

        db.ChapterWords.AddRange(
            new ChapterWord { ChapterId = 1, WordPairId = 1, Count = 5 },
            new ChapterWord { ChapterId = 1, WordPairId = 2, Count = 4 },
            new ChapterWord { ChapterId = 1, WordPairId = 3, Count = 3 },
            new ChapterWord { ChapterId = 2, WordPairId = 4, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 5, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 6, Count = 1 });

        foreach (var word in words)
        {
            db.UnknownWords.Add(new UnknownWord { Id = word.Id, UserId = UserId, WordPairId = word.Id });
        }

        db.KnownWords.Add(new KnownWord { Id = 1, UserId = UserId, WordPairId = 3 });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// Частоти для перевірки порядку. Словник заповнюється через DictionaryWords (не через навігацію),
    /// бо саме там живе книжкова частота. Усі шість слів перекладені й «не знаю».
    /// Книжка: silo 15, abbey 7, holston 7, abide 3, cleaning 2, jahns 1.
    /// Глава 1: silo 10, abide 3, cleaning 2. Глава 2: abbey 7, holston 7, silo 5, jahns 1.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeWithFrequenciesAsync()
    {
        var db = NewContext();

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.Add(new LanguageLab.Domain.Entities.Dictionary { Id = DictionaryId, Name = "Wool", WordsCount = 6 });

        db.Words.AddRange(
            Word(1, "silo", "бункер"), Word(2, "abide", "дотримуватися"), Word(3, "cleaning", "чистка"),
            Word(4, "holston", "холстон (ім'я)"), Word(5, "jahns", "янс (ім'я)"), Word(6, "abbey", "абатство"));

        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 1, Frequency = 15 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 2, Frequency = 3 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 3, Frequency = 2 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 4, Frequency = 7 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 5, Frequency = 1 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 6, Frequency = 7 });

        db.Chapters.Add(new Chapter { Id = 1, DictionaryId = DictionaryId, Order = 0, Title = "One", WordsCount = 3 });
        db.Chapters.Add(new Chapter { Id = 2, DictionaryId = DictionaryId, Order = 1, Title = "Two", WordsCount = 4 });

        db.ChapterWords.AddRange(
            new ChapterWord { ChapterId = 1, WordPairId = 1, Count = 10 },
            new ChapterWord { ChapterId = 1, WordPairId = 2, Count = 3 },
            new ChapterWord { ChapterId = 1, WordPairId = 3, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 1, Count = 5 },
            new ChapterWord { ChapterId = 2, WordPairId = 4, Count = 7 },
            new ChapterWord { ChapterId = 2, WordPairId = 5, Count = 1 },
            new ChapterWord { ChapterId = 2, WordPairId = 6, Count = 7 });

        for (long id = 1; id <= 6; id++)
        {
            db.UnknownWords.Add(new UnknownWord { Id = id, UserId = UserId, WordPairId = id, CreatedAt = Now });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task NewBatch_ReturnsOnlyLearnableWords()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSelectionService(db);

        var batch = await service.GetNewBatchAsync(UserId, DictionaryId, size: 5);

        Assert.Equal(new[] { 1L, 2L }, batch.Select(w => w.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task NewBatch_NeverExceedsRequestedSize()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSelectionService(db);

        var batch = await service.GetNewBatchAsync(UserId, DictionaryId, size: 1);

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

        var batch = await new WordSelectionService(db).GetNewBatchAsync(UserId, DictionaryId, size: 5);

        Assert.Empty(batch);
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

    /// <summary>
    /// Виключене слово не вчиться, навіть якщо воно лежить у UnknownWords:
    /// інакше бот далі показував би те, що юзер викинув у вебі.
    /// </summary>
    [Fact]
    public async Task Excluded_word_never_enters_a_new_batch()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSelectionService(db);

        var batch = await service.GetNewBatchAsync(UserId, DictionaryId, size: 10);

        Assert.DoesNotContain(batch, w => w.Word == "solo");
    }

    [Fact]
    public async Task NewBatch_WithChapterScope_ReturnsOnlyLearnableWordsOfThoseChapters()
    {
        await using var db = await ArrangeWithChaptersAsync();
        var service = new WordSelectionService(db);

        var chapterOne = await service.GetNewBatchAsync(UserId, DictionaryId, size: 10, chapterIds: [1]);
        var chapterTwo = await service.GetNewBatchAsync(UserId, DictionaryId, size: 10, chapterIds: [2]);

        Assert.Equal(new[] { 1L, 2L }, chapterOne.Select(w => w.Id).OrderBy(id => id));
        Assert.Equal(new[] { 4L, 5L }, chapterTwo.Select(w => w.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task NewBatch_WithNullOrEmptyChapterScope_MeansWholeDictionary()
    {
        await using var db = await ArrangeWithChaptersAsync();
        var service = new WordSelectionService(db);

        var nullScope = await service.GetNewBatchAsync(UserId, DictionaryId, size: 10);
        var emptyScope = await service.GetNewBatchAsync(UserId, DictionaryId, size: 10, chapterIds: []);

        Assert.Equal(new[] { 1L, 2L, 4L, 5L }, nullScope.Select(w => w.Id).OrderBy(id => id));
        Assert.Equal(new[] { 1L, 2L, 4L, 5L }, emptyScope.Select(w => w.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task CountLearnable_HonoursChapterScope()
    {
        await using var db = await ArrangeWithChaptersAsync();
        var service = new WordSelectionService(db);

        Assert.Equal(4, await service.CountLearnableAsync(UserId, DictionaryId));
        Assert.Equal(2, await service.CountLearnableAsync(UserId, DictionaryId, chapterIds: [1]));
        Assert.Equal(2, await service.CountLearnableAsync(UserId, DictionaryId, chapterIds: [2]));
        Assert.Equal(4, await service.CountLearnableAsync(UserId, DictionaryId, chapterIds: [1, 2]));
    }

    [Fact]
    public async Task Candidates_BookScope_OrderedByBookFrequency_ThenWord()
    {
        await using var db = await ArrangeWithFrequenciesAsync();

        var candidates = await new WordSelectionService(db).GetCandidatesAsync(UserId, DictionaryId, chapterIds: null, take: 10);

        Assert.Equal(new long[] { 1, 6, 4, 2, 3, 5 }, candidates.Select(c => c.WordPairId));
        Assert.Equal(new[] { 15, 7, 7, 3, 2, 1 }, candidates.Select(c => c.Frequency));
        Assert.Equal("бункер", candidates[0].Translation);
    }

    [Fact]
    public async Task Candidates_ChapterScope_UsesChapterFrequency()
    {
        await using var db = await ArrangeWithFrequenciesAsync();
        var service = new WordSelectionService(db);

        var one = await service.GetCandidatesAsync(UserId, DictionaryId, chapterIds: [1], take: 10);
        var two = await service.GetCandidatesAsync(UserId, DictionaryId, chapterIds: [2], take: 10);
        var both = await service.GetCandidatesAsync(UserId, DictionaryId, chapterIds: [1, 2], take: 10);

        Assert.Equal(new long[] { 1, 2, 3 }, one.Select(c => c.WordPairId));
        Assert.Equal(new[] { 10, 3, 2 }, one.Select(c => c.Frequency));
        Assert.Equal(new long[] { 6, 4, 1, 5 }, two.Select(c => c.WordPairId));
        Assert.Equal(new[] { 7, 7, 5, 1 }, two.Select(c => c.Frequency));
        // Дві глави — сума їхніх лічильників, а не книжкова частота.
        Assert.Equal(new long[] { 1, 6, 4, 2, 3, 5 }, both.Select(c => c.WordPairId));
        Assert.Equal(15, both[0].Frequency);
    }

    [Fact]
    public async Task Candidates_TakeIsClamped()
    {
        await using var db = await ArrangeWithFrequenciesAsync();
        var service = new WordSelectionService(db);

        Assert.Equal(2, (await service.GetCandidatesAsync(UserId, DictionaryId, null, take: 2)).Count);
        Assert.Single(await service.GetCandidatesAsync(UserId, DictionaryId, null, take: 0));
        Assert.Equal(6, (await service.GetCandidatesAsync(UserId, DictionaryId, null, take: 100)).Count);
    }

    [Fact]
    public async Task NewBatch_IsTheTopOfCandidates_InTheSameOrder()
    {
        await using var db = await ArrangeWithFrequenciesAsync();

        var batch = await new WordSelectionService(db).GetNewBatchAsync(UserId, DictionaryId, size: 3);

        Assert.Equal(new long[] { 1, 6, 4 }, batch.Select(w => w.Id));
    }

    [Fact]
    public async Task LearnableByIds_KeepsRequestedOrder_DropsForeignNotLearnableAndDuplicates()
    {
        await using var db = await ArrangeAsync();

        // 99 — не існує; 4 — уже з прогресом; 3 — «знаю»; 5 — без перекладу; 8 — виключене; 2 — двічі.
        var words = await new WordSelectionService(db)
            .GetLearnableByIdsAsync(UserId, DictionaryId, chapterIds: null, ids: [2, 99, 4, 1, 2, 3, 5, 8]);

        Assert.Equal(new long[] { 2, 1 }, words.Select(w => w.Id));
    }

    [Fact]
    public async Task LearnableByIds_HonoursChapterScope()
    {
        await using var db = await ArrangeWithChaptersAsync();

        var words = await new WordSelectionService(db)
            .GetLearnableByIdsAsync(UserId, DictionaryId, chapterIds: [1], ids: [4, 1]);

        Assert.Equal(new long[] { 1 }, words.Select(w => w.Id));
    }

    [Fact]
    public async Task LearnableByIds_EmptyInput_IsEmpty()
    {
        await using var db = await ArrangeAsync();

        Assert.Empty(await new WordSelectionService(db).GetLearnableByIdsAsync(UserId, DictionaryId, null, ids: []));
    }
}
