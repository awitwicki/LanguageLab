using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class WordSortingServiceTests
{
    private const long UserId = 1;
    private const long DictionaryId = 1;
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Книжка з двох глав і п'яти слів. Глава 1: silo(10), abide(3), cleaning(2).
    /// Глава 2: silo(5), holston(7), jahns(1). Частоти по книжці — сума.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });

        var dictionary = new Domain.Entities.Dictionary { Id = DictionaryId, Name = "Wool", WordsCount = 5 };
        db.Dictionaries.Add(dictionary);

        var words = new[]
        {
            new WordPair { Id = 1, Word = "silo", Translation = "" },
            new WordPair { Id = 2, Word = "abide", Translation = "" },
            new WordPair { Id = 3, Word = "cleaning", Translation = "" },
            new WordPair { Id = 4, Word = "holston", Translation = "" },
            new WordPair { Id = 5, Word = "jahns", Translation = "" }
        };
        db.Words.AddRange(words);

        db.Chapters.Add(new Chapter { Id = 1, DictionaryId = DictionaryId, Order = 0, Title = "One", WordsCount = 3 });
        db.Chapters.Add(new Chapter { Id = 2, DictionaryId = DictionaryId, Order = 1, Title = "Two", WordsCount = 3 });

        db.ChapterWords.AddRange(
            new ChapterWord { ChapterId = 1, WordPairId = 1, Count = 10 },
            new ChapterWord { ChapterId = 1, WordPairId = 2, Count = 3 },
            new ChapterWord { ChapterId = 1, WordPairId = 3, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 1, Count = 5 },
            new ChapterWord { ChapterId = 2, WordPairId = 4, Count = 7 },
            new ChapterWord { ChapterId = 2, WordPairId = 5, Count = 1 });

        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 1, Frequency = 15 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 2, Frequency = 3 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 3, Frequency = 2 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 4, Frequency = 7 },
            new DictionaryWord { DictionaryId = DictionaryId, WordPairId = 5, Frequency = 1 });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// Словник із <paramref name="count"/> незапакованих слів — щоб довести стелю
    /// MaxTake реальним надлишком даних, а не випадковим збігом через малу фікстуру.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeManyWordsAsync(int count)
    {
        var db = NewContext();

        db.Users.Add(new TelegramUser { Id = UserId, TelegramUserId = 1111111111 });
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = DictionaryId, Name = "Big", WordsCount = count });

        for (var i = 1; i <= count; i++)
        {
            db.Words.Add(new WordPair { Id = i, Word = $"word{i}", Translation = "" });
            db.DictionaryWords.Add(new DictionaryWord { DictionaryId = DictionaryId, WordPairId = i, Frequency = i });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Queue_is_ordered_by_frequency_descending()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: null, take: 50);

        Assert.Equal(["silo", "holston", "abide", "cleaning", "jahns"], queue.Words.Select(w => w.Word));
    }

    [Fact]
    public async Task Queue_skips_words_from_all_three_shelves()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = UserId, WordPairId = 1, CreatedAt = Now });
        db.UnknownWords.Add(new UnknownWord { Id = 1, UserId = UserId, WordPairId = 4, CreatedAt = Now });
        db.ExcludedWords.Add(new ExcludedWord { Id = 1, UserId = UserId, WordPairId = 5, CreatedAt = Now });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: null, take: 50);

        Assert.Equal(["abide", "cleaning"], queue.Words.Select(w => w.Word));
    }

    [Fact]
    public async Task Progress_counts_all_three_shelves_as_sorted()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = UserId, WordPairId = 1, CreatedAt = Now });
        db.ExcludedWords.Add(new ExcludedWord { Id = 1, UserId = UserId, WordPairId = 5, CreatedAt = Now });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: null, take: 50);

        Assert.Equal(5, queue.Total);
        Assert.Equal(2, queue.Sorted);
        Assert.Equal(3, queue.Remaining);
    }

    [Fact]
    public async Task Chapter_filter_narrows_both_queue_and_progress()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: [2], take: 50);

        // Частота лишається книжковою (silo = 15), а склад — главним.
        Assert.Equal(["silo", "holston", "jahns"], queue.Words.Select(w => w.Word));
        Assert.Equal(3, queue.Total);
        Assert.Equal(0, queue.Sorted);
    }

    [Fact]
    public async Task Take_is_clamped_to_max()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: null, take: 10_000);

        Assert.Equal(5, queue.Words.Count);
    }

    [Fact]
    public async Task Queue_never_exceeds_MaxTake_even_when_more_words_are_unsorted()
    {
        // На відміну від Take_is_clamped_to_max (де в словнику лише 5 слів,
        // і Take(10_000) та Take(200) дають однаковий результат), тут
        // незапакованих слів свідомо більше за MaxTake — інакше тест пройде
        // навіть без Math.Clamp у реалізації.
        await using var db = await ArrangeManyWordsAsync(WordSortingService.MaxTake + 50);
        var service = new WordSortingService(db);

        var queue = await service.GetQueueAsync(UserId, DictionaryId, chapterIds: null, take: 10_000);

        Assert.Equal(WordSortingService.MaxTake, queue.Words.Count);
    }

    [Fact]
    public async Task Chapter_progress_is_reported_per_chapter()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = UserId, WordPairId = 1, CreatedAt = Now });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);

        var progress = await service.GetChapterProgressAsync(UserId, DictionaryId);

        // silo є в обох главах, тож обидві просунулись на одиницю.
        Assert.Equal([(1L, 3, 1), (2L, 3, 1)], progress.Select(p => (p.ChapterId, p.Total, p.Sorted)));
    }

    [Fact]
    public async Task Mark_puts_the_word_on_the_requested_shelf()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);

        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == UserId && k.WordPairId == 1));
    }

    /// <summary>
    /// Полиці взаємовиключні. Слово, що лежить водночас у KnownWords і UnknownWords,
    /// назавжди випадає з навчання: LearnableQuery вимагає «є в unknown І немає в known».
    /// </summary>
    [Fact]
    public async Task Mark_removes_the_word_from_the_other_two_shelves()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Unknown, Now);
        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now.AddSeconds(1));

        Assert.False(await db.UnknownWords.AnyAsync(u => u.UserId == UserId && u.WordPairId == 1));
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == UserId && k.WordPairId == 1));
    }

    /// <summary>Повторна та сама позначка не має підіймати слово вгору колонки «останні 10».</summary>
    [Fact]
    public async Task Marking_the_same_shelf_twice_keeps_the_original_timestamp()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);
        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now.AddHours(5));

        var createdAt = await db.KnownWords
            .Where(k => k.UserId == UserId && k.WordPairId == 1)
            .Select(k => k.CreatedAt)
            .SingleAsync();

        Assert.Equal(Now, createdAt);
    }

    [Fact]
    public async Task Undo_removes_the_newest_mark_across_all_shelves()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Unknown, Now.AddSeconds(1));
        await service.MarkAsync(UserId, wordPairId: 3, SortStatus.Excluded, Now.AddSeconds(2));

        var undone = await service.UndoAsync(UserId);

        Assert.NotNull(undone);
        Assert.Equal(3, undone.WordPairId);
        Assert.Equal("cleaning", undone.Word);
        Assert.Equal(SortStatus.Excluded, undone.PreviousStatus);
        Assert.False(await db.ExcludedWords.AnyAsync(e => e.WordPairId == 3));
    }

    [Fact]
    public async Task Undo_is_repeatable()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Unknown, Now.AddSeconds(1));

        await service.UndoAsync(UserId);
        var second = await service.UndoAsync(UserId);

        Assert.Equal(1, second!.WordPairId);
        Assert.Equal(0, await db.KnownWords.CountAsync());
        Assert.Equal(0, await db.UnknownWords.CountAsync());
    }

    [Fact]
    public async Task Undo_returns_null_when_there_is_nothing_to_undo()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        Assert.Null(await service.UndoAsync(UserId));
    }

    /// <summary>Undo відкочує позначки лише цього юзера.</summary>
    [Fact]
    public async Task Undo_ignores_marks_of_other_users()
    {
        await using var db = await ArrangeAsync();
        db.Users.Add(new TelegramUser { Id = 2, TelegramUserId = 2222222222 });
        db.KnownWords.Add(new KnownWord { Id = 99, UserId = 2, WordPairId = 4, CreatedAt = Now.AddDays(1) });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);
        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);

        var undone = await service.UndoAsync(UserId);

        Assert.Equal(1, undone!.WordPairId);
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == 2));
    }

    [Fact]
    public async Task Recent_returns_newest_first_and_excludes_the_excluded_shelf()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);
        await service.MarkAsync(UserId, wordPairId: 4, SortStatus.Known, Now.AddSeconds(1));
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Unknown, Now.AddSeconds(2));
        await service.MarkAsync(UserId, wordPairId: 3, SortStatus.Excluded, Now.AddSeconds(3));

        var recent = await service.GetRecentAsync(UserId, take: 10);

        Assert.Equal(["holston", "silo"], recent.Known.Select(w => w.Word));
        Assert.Equal(["abide"], recent.Unknown.Select(w => w.Word));
    }

    [Fact]
    public async Task Recent_respects_take()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);
        await service.MarkAsync(UserId, wordPairId: 4, SortStatus.Known, Now.AddSeconds(1));

        var recent = await service.GetRecentAsync(UserId, take: 1);

        Assert.Equal(["holston"], recent.Known.Select(w => w.Word));
    }
}
