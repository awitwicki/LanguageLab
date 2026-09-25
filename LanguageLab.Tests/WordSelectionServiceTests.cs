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
    /// One graph holding a representative of every exclusion rule.
    /// Only words 1 and 2 are learnable.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();

        var silo = new LanguageLab.Domain.Entities.Dictionary
        { Id = 1, Name = "silo1", WordsCount = 6, PublicationStatus = PublicationStatus.Published };
        var other = new LanguageLab.Domain.Entities.Dictionary
        { Id = 2, Name = "other", WordsCount = 1, PublicationStatus = PublicationStatus.Published };

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

        // Everything except word 7 the user marked as "want to learn".
        for (long wordPairId = 1; wordPairId <= 6; wordPairId++)
        {
            db.UnknownWords.Add(new UnknownWord { Id = wordPairId, UserId = UserId, WordPairId = wordPairId });
        }

        // Word 8 the user marked both as "want to learn" and as excluded — exclusion must win.
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
    /// A two-chapter book. Chapter 1: words 1, 2, 3; chapter 2: words 4, 5, 6.
    /// All on the "want to learn" shelf, but the user already knows word 3 and word 6 has
    /// no translation — so the learnable ones are 1, 2 (chapter 1) and 4, 5 (chapter 2).
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
    /// Frequencies for checking the order. The dictionary is filled through DictionaryWords (not the
    /// navigation) because that is where the book frequency lives. All six words are translated and "don't know".
    /// Book: silo 15, abbey 7, holston 7, abide 3, cleaning 2, jahns 1.
    /// Chapter 1: silo 10, abide 3, cleaning 2. Chapter 2: abbey 7, holston 7, silo 5, jahns 1.
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

    /// <summary>
    /// Chapter seed plus progress: word1 (chapter 1) and word4 (chapter 2) are due, word2
    /// (chapter 1) waits two more days, word5 (chapter 2) is learned.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeWithDueWordsInChaptersAsync()
    {
        var db = await ArrangeWithChaptersAsync();
        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = 1, Box = 1, DueAt = Now.AddDays(-1), LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = 2, Box = 2, DueAt = Now.AddDays(2), LastSeenAt = Now },
            new WordProgress { Id = 3, UserId = UserId, WordPairId = 4, Box = 1, DueAt = Now.AddDays(-3), LastSeenAt = Now },
            new WordProgress { Id = 4, UserId = UserId, WordPairId = 5, Box = 5, DueAt = null, IsLearned = true, LastSeenAt = Now });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task DueWords_HonourDictionaryAndChapterScope()
    {
        await using var db = await ArrangeWithDueWordsInChaptersAsync();
        var service = new WordSelectionService(db);

        Assert.Equal(new[] { 4L, 1L }, (await service.GetDueWordsAsync(UserId, Now, 20)).Select(w => w.Id));
        Assert.Equal(new[] { 4L, 1L }, (await service.GetDueWordsAsync(UserId, Now, 20, DictionaryId)).Select(w => w.Id));
        Assert.Equal(new[] { 1L }, (await service.GetDueWordsAsync(UserId, Now, 20, DictionaryId, [1])).Select(w => w.Id));
        Assert.Equal(new[] { 4L }, (await service.GetDueWordsAsync(UserId, Now, 20, DictionaryId, [2])).Select(w => w.Id));
        Assert.Empty(await service.GetDueWordsAsync(UserId, Now, 20, dictionaryId: 999));
    }

    [Fact]
    public async Task CountDue_HonoursScope()
    {
        await using var db = await ArrangeWithDueWordsInChaptersAsync();
        var service = new WordSelectionService(db);

        Assert.Equal(2, await service.CountDueAsync(UserId, Now));
        Assert.Equal(1, await service.CountDueAsync(UserId, Now, DictionaryId, [1]));
        Assert.Equal(0, await service.CountDueAsync(UserId, Now, dictionaryId: 999));
    }

    /// <summary>
    /// What the chapter row needs to decide between "Review", "next review tomorrow" and
    /// nothing: how many wait now, and when the next one comes due if none do.
    /// </summary>
    [Fact]
    public async Task ReviewAvailabilityByChapter_CountsDueAndNamesTheNextDueDate()
    {
        await using var db = await ArrangeWithDueWordsInChaptersAsync();

        var byChapter = await new WordSelectionService(db).GetReviewAvailabilityByChapterAsync(UserId, DictionaryId, Now);

        Assert.Equal(new ReviewAvailability(1, Now.AddDays(2)), byChapter[1]);
        Assert.Equal(new ReviewAvailability(1, null), byChapter[2]);
    }

    /// <summary>A chapter where nothing is in progress simply has no entry — the caller falls back to None.</summary>
    [Fact]
    public async Task ReviewAvailabilityByChapter_OmitsChaptersWithoutProgress()
    {
        await using var db = await ArrangeWithChaptersAsync();

        var byChapter = await new WordSelectionService(db).GetReviewAvailabilityByChapterAsync(UserId, DictionaryId, Now);

        Assert.Empty(byChapter);
        Assert.Equal(new ReviewAvailability(0, null), ReviewAvailability.None);
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

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(UserId, DictionaryId, size: 60, new Random(1));

        Assert.DoesNotContain(pool, w => w.Translation.Length == 0);
    }

    [Fact]
    public async Task DistractorPool_WithoutDictionary_SpansAllWords()
    {
        await using var db = await ArrangeAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(UserId, dictionaryId: null, size: 60, new Random(1));

        Assert.Contains(pool, w => w.Id == 6);
    }

    /// <summary>
    /// silo1 has six translated words; a pool of 60 cannot come from it alone, so the shared
    /// vocabulary fills the rest — but the dictionary's own words come first, so a real book
    /// still gets natural distractors.
    /// </summary>
    [Fact]
    public async Task DistractorPool_TopsUpFromSharedWords_WhenTheDictionaryIsSmall()
    {
        await using var db = await ArrangeAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(UserId, DictionaryId, size: 60, new Random(1));

        var siloIds = new long[] { 1, 2, 3, 4, 7, 8 };
        Assert.Equal(7, pool.Count);
        Assert.All(pool.Take(6), w => Assert.Contains(w.Id, siloIds));
        Assert.Equal(6, pool[6].Id);
    }

    [Fact]
    public async Task DistractorPool_DoesNotTopUp_WhenTheDictionaryFillsIt()
    {
        await using var db = await ArrangeAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(UserId, DictionaryId, size: 3, new Random(1));

        Assert.Equal(3, pool.Count);
        Assert.DoesNotContain(pool, w => w.Id == 6);
    }

    /// <summary>Another user's personal words are their private notes: never a distractor for me. My own are fine.</summary>
    [Fact]
    public async Task DistractorPool_NeverShowsAnotherUsersOwnedWords()
    {
        await using var db = await ArrangeAsync();
        db.Users.Add(new TelegramUser { Id = 2, TelegramUserId = 2222 });

        // Each word sits in its owner's own personal dictionary, as it would in production —
        // a personal word never floats free of a Dictionary row.
        var theirPersonal = new Domain.Entities.Dictionary
        { Name = "My words", WordsCount = 1, OwnerId = 2, PublicationStatus = PublicationStatus.Private, IsPersonal = true };
        var myPersonal = new Domain.Entities.Dictionary
        { Name = "My words", WordsCount = 1, OwnerId = UserId, PublicationStatus = PublicationStatus.Private, IsPersonal = true };
        theirPersonal.Words = [new WordPair { Id = 20, Word = "theirs", Translation = "їхнє", OwnerId = 2 }];
        myPersonal.Words = [new WordPair { Id = 21, Word = "mine", Translation = "моє", OwnerId = UserId }];
        db.Dictionaries.AddRange(theirPersonal, myPersonal);
        await db.SaveChangesAsync();

        var pool = await new WordSelectionService(db).GetDistractorPoolAsync(UserId, dictionaryId: null, size: 60, new Random(1));

        Assert.DoesNotContain(pool, w => w.Id == 20);
        Assert.Contains(pool, w => w.Id == 21);
    }

    /// <summary>
    /// An excluded word is not learned even if it sits in UnknownWords:
    /// otherwise the bot would keep showing what the user threw out on the web.
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
        // Two chapters — the sum of their counts, not the book frequency.
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

        // 99 — does not exist; 4 — already has progress; 3 — "know"; 5 — no translation; 8 — excluded; 2 — twice.
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

    [Fact]
    public async Task The_pool_never_reaches_into_a_dictionary_the_user_cannot_see()
    {
        await using var db = NewContext();

        var mine = new Domain.Entities.Dictionary { Name = "Mine", WordsCount = 1, OwnerId = 1, PublicationStatus = PublicationStatus.Private };
        var theirs = new Domain.Entities.Dictionary { Name = "Theirs", WordsCount = 1, OwnerId = 2, PublicationStatus = PublicationStatus.Private };
        mine.Words = [new WordPair { Word = "silo", Translation = "бункер" }];
        theirs.Words = [new WordPair { Word = "secret", Translation = "таємниця" }];

        db.Dictionaries.AddRange(mine, theirs);

        // A word cached by a reader lookup that never joined a dictionary: also out of the pool.
        db.Words.Add(new WordPair { Word = "orphan", Translation = "сирота" });
        await db.SaveChangesAsync();

        var service = new WordSelectionService(db);
        var pool = await service.GetDistractorPoolAsync(userId: 1, dictionaryId: mine.Id, size: 60, new Random(1));

        Assert.DoesNotContain(pool, w => w.Word == "secret");
        Assert.DoesNotContain(pool, w => w.Word == "orphan");
        Assert.Contains(pool, w => w.Word == "silo");
    }

    [Fact]
    public async Task A_public_dictionarys_words_still_fill_the_pool()
    {
        await using var db = NewContext();

        var personal = new Domain.Entities.Dictionary
        { Name = "My words", WordsCount = 1, OwnerId = 1, PublicationStatus = PublicationStatus.Private, IsPersonal = true };
        personal.Words = [new WordPair { Word = "silo", Translation = "бункер", OwnerId = 1 }];

        var shared = new Domain.Entities.Dictionary { Name = "Wool", WordsCount = 1, PublicationStatus = PublicationStatus.Published };
        shared.Words = [new WordPair { Word = "cleaning", Translation = "чистка" }];

        db.Dictionaries.AddRange(personal, shared);
        await db.SaveChangesAsync();

        var service = new WordSelectionService(db);
        var pool = await service.GetDistractorPoolAsync(userId: 1, dictionaryId: personal.Id, size: 60, new Random(1));

        Assert.Contains(pool, w => w.Word == "cleaning");
        Assert.Contains(pool, w => w.Word == "silo");
    }
}
