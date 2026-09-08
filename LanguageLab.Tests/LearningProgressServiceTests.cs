using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class LearningProgressServiceTests
{
    private const long UserId = 1;
    private const long DictionaryId = 1;
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static WordPair Word(long id, string word, string translation = "переклад") =>
        new() { Id = id, Word = word, Translation = translation };

    private static WordProgress Progress(long id, long wordPairId, int box, bool isLearned = false) =>
        new()
        {
            Id = id,
            UserId = UserId,
            WordPairId = wordPairId,
            Box = box,
            IsLearned = isLearned,
            DueAt = isLearned ? null : Now.AddDays(1),
            LastSeenAt = Now
        };

    /// <summary>
    /// Книжка з трьох глав, по одному представнику кожного правила:
    ///  1 silo     — «не знаю», без прогресу           → не почато (глави 1 і 2)
    ///  2 abide    — «не знаю», бокс 2                  → бокс 2    (глава 1)
    ///  3 cleaning — «не знаю», бокс 4                  → бокс 4    (глава 1)
    ///  4 holston  — «знаю» + прогрес IsLearned         → вивчено   (глава 2)
    ///  5 jahns    — «не знаю», без перекладу           → поза множиною (глава 2)
    ///  6 solo     — «не знаю» + виключене, бокс 1      → поза множиною (глава 2)
    ///  7 able     — «знаю», без прогресу               → поза множиною (глава 1)
    ///  8 wool     — не сортоване                       → поза множиною (глава 3 — лише воно)
    ///  9 dune     — «не знаю», але в іншому словнику   → поза скоупом
    /// 10 lift     — «не знаю», бокс 5, ще не вивчене   → бокс 5    (глава 2)
    /// 11 abyss    — «не знаю», без прогресу            → не почато (глава 1)
    /// Книжка: не почато 2, бокси [0,1,0,1,1], вивчено 1, разом 6.
    /// Глава 1: не почато 2, бокси [0,1,0,1,0], вивчено 0, разом 4.
    /// Глава 2: не почато 1, бокси [0,0,0,0,1], вивчено 1, разом 3.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();
        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });

        var words = new[]
        {
            Word(1, "silo"), Word(2, "abide"), Word(3, "cleaning"), Word(4, "holston"), Word(5, "jahns", ""),
            Word(6, "solo"), Word(7, "able"), Word(8, "wool"), Word(10, "lift"), Word(11, "abyss")
        };

        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = DictionaryId, Name = "Wool", WordsCount = words.Length, Words = words
        });
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = 2, Name = "Other", WordsCount = 1, Words = [Word(9, "dune")]
        });

        db.Chapters.AddRange(
            new Chapter { Id = 1, DictionaryId = DictionaryId, Order = 0, Title = "One", WordsCount = 5 },
            new Chapter { Id = 2, DictionaryId = DictionaryId, Order = 1, Title = "Two", WordsCount = 5 },
            new Chapter { Id = 3, DictionaryId = DictionaryId, Order = 2, Title = "Three", WordsCount = 1 });

        db.ChapterWords.AddRange(
            new ChapterWord { ChapterId = 1, WordPairId = 1, Count = 5 },
            new ChapterWord { ChapterId = 1, WordPairId = 2, Count = 3 },
            new ChapterWord { ChapterId = 1, WordPairId = 3, Count = 2 },
            new ChapterWord { ChapterId = 1, WordPairId = 7, Count = 1 },
            new ChapterWord { ChapterId = 1, WordPairId = 11, Count = 1 },
            new ChapterWord { ChapterId = 2, WordPairId = 1, Count = 4 },
            new ChapterWord { ChapterId = 2, WordPairId = 4, Count = 3 },
            new ChapterWord { ChapterId = 2, WordPairId = 5, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 6, Count = 1 },
            new ChapterWord { ChapterId = 2, WordPairId = 10, Count = 1 },
            new ChapterWord { ChapterId = 3, WordPairId = 8, Count = 1 });

        foreach (var id in new long[] { 1, 2, 3, 5, 6, 9, 10, 11 })
        {
            db.UnknownWords.Add(new UnknownWord { Id = id, UserId = UserId, WordPairId = id, CreatedAt = Now });
        }

        db.KnownWords.AddRange(
            new KnownWord { Id = 1, UserId = UserId, WordPairId = 4, CreatedAt = Now },
            new KnownWord { Id = 2, UserId = UserId, WordPairId = 7, CreatedAt = Now });

        db.ExcludedWords.Add(new ExcludedWord { Id = 1, UserId = UserId, WordPairId = 6, CreatedAt = Now });

        db.WordProgresses.AddRange(
            Progress(1, 2, box: 2),
            Progress(2, 3, box: 4),
            Progress(3, 4, box: 5, isLearned: true),
            Progress(4, 6, box: 1),
            Progress(5, 10, box: 5));

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Book_scope_counts_each_rule_once()
    {
        await using var db = await ArrangeAsync();

        var progress = await new LearningProgressService(db).GetAsync(UserId, DictionaryId);

        Assert.Equal(2, progress.NotStarted);
        Assert.Equal(new[] { 0, 1, 0, 1, 1 }, progress.Boxes);
        Assert.Equal(1, progress.Learned);
        Assert.Equal(6, progress.Total);
    }

    [Fact]
    public async Task Chapter_scope_counts_only_words_of_that_chapter()
    {
        await using var db = await ArrangeAsync();
        var service = new LearningProgressService(db);

        var one = await service.GetAsync(UserId, DictionaryId, chapterIds: [1]);
        var two = await service.GetAsync(UserId, DictionaryId, chapterIds: [2]);

        Assert.Equal((2, 0, 4), (one.NotStarted, one.Learned, one.Total));
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, one.Boxes);
        Assert.Equal((1, 1, 3), (two.NotStarted, two.Learned, two.Total));
        Assert.Equal(new[] { 0, 0, 0, 0, 1 }, two.Boxes);
    }

    [Fact]
    public async Task Two_chapters_count_a_shared_word_once_and_equal_the_book()
    {
        await using var db = await ArrangeAsync();
        var service = new LearningProgressService(db);

        var both = await service.GetAsync(UserId, DictionaryId, chapterIds: [1, 2]);
        var book = await service.GetAsync(UserId, DictionaryId);

        Assert.Equal(book, both with { Boxes = book.Boxes });
        Assert.Equal(book.Boxes, both.Boxes);
    }

    [Fact]
    public async Task Scope_without_tracked_words_is_all_zero()
    {
        await using var db = await ArrangeAsync();

        var three = await new LearningProgressService(db).GetAsync(UserId, DictionaryId, chapterIds: [3]);

        Assert.Equal(0, three.Total);
        Assert.Equal(LearningProgress.Empty.Boxes, three.Boxes);
    }

    /// <summary>
    /// «Не почато» і «до вивчення» — одне число: сірий сегмент шкали має збігатися
    /// з підрядком глави. Якщо предикати розійдуться — падає саме цей тест.
    /// </summary>
    [Fact]
    public async Task NotStarted_equals_CountLearnable_for_every_scope()
    {
        await using var db = await ArrangeAsync();
        var progress = new LearningProgressService(db);
        var selection = new WordSelectionService(db);

        foreach (var scope in new IReadOnlyList<long>?[] { null, [1], [2], [1, 2], [3] })
        {
            Assert.Equal(
                await selection.CountLearnableAsync(UserId, DictionaryId, scope),
                (await progress.GetAsync(UserId, DictionaryId, scope)).NotStarted);
        }
    }

    [Fact]
    public async Task ByChapter_matches_GetAsync_and_skips_chapters_without_tracked_words()
    {
        await using var db = await ArrangeAsync();
        var service = new LearningProgressService(db);

        var byChapter = await service.GetByChapterAsync(UserId, DictionaryId);

        Assert.Equal(new long[] { 1, 2 }, byChapter.Keys.OrderBy(id => id));

        foreach (var chapterId in new long[] { 1, 2 })
        {
            var direct = await service.GetAsync(UserId, DictionaryId, chapterIds: [chapterId]);
            Assert.Equal(direct.NotStarted, byChapter[chapterId].NotStarted);
            Assert.Equal(direct.Boxes, byChapter[chapterId].Boxes);
            Assert.Equal(direct.Learned, byChapter[chapterId].Learned);
        }
    }
}
