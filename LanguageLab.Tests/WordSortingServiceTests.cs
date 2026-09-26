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
    /// A two-chapter, five-word book. Chapter 1: silo(10), abide(3), cleaning(2).
    /// Chapter 2: silo(5), holston(7), jahns(1). Book frequencies are the sum.
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
    /// A dictionary of <paramref name="count"/> unsorted words — to prove the MaxTake
    /// ceiling with a real surplus of data, not a coincidence of a small fixture.
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

        // Frequency stays the book's (silo = 15) while the membership is the chapter's.
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
        // Unlike Take_is_clamped_to_max (where the dictionary has only 5 words,
        // so Take(10_000) and Take(200) give the same result), here there are
        // deliberately more unsorted words than MaxTake — otherwise the test
        // would pass even without Math.Clamp in the implementation.
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

        // silo is in both chapters, so both advanced by one.
        Assert.Equal([(1L, 3, 1), (2L, 3, 1)], progress.Select(p => (p.ChapterId, p.Total, p.Sorted)));
    }

    [Fact]
    public async Task Mark_puts_the_word_on_the_requested_shelf()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var marked = await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);

        Assert.True(marked);
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == UserId && k.WordPairId == 1));
    }

    /// <summary>
    /// A personal word belongs to its owner alone — this is what stops one user from
    /// enumerating WordPair ids, marking a stranger's private word, and reading its text
    /// back out through their own GetRecentAsync/UndoAsync.
    /// </summary>
    [Fact]
    public async Task Mark_refuses_a_word_pair_owned_by_someone_else()
    {
        await using var db = await ArrangeAsync();
        db.Users.Add(new TelegramUser { Id = 2, TelegramUserId = 2222222222 });
        db.Words.Add(new WordPair { Id = 6, Word = "secret", Translation = "таємниця", OwnerId = 2 });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);

        var marked = await service.MarkAsync(UserId, wordPairId: 6, SortStatus.Known, Now);

        Assert.False(marked);
        Assert.False(await db.KnownWords.AnyAsync(k => k.WordPairId == 6));
    }

    /// <summary>An id that doesn't exist at all is refused the same way — no distinction visible to the caller.</summary>
    [Fact]
    public async Task Mark_refuses_an_unknown_word_pair_id()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var marked = await service.MarkAsync(UserId, wordPairId: 999, SortStatus.Known, Now);

        Assert.False(marked);
    }

    /// <summary>
    /// The shelves are mutually exclusive. A word sitting in both KnownWords and UnknownWords
    /// drops out of learning for good: LearnableQuery demands "in unknown AND not in known".
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

    /// <summary>Repeating the same mark must not lift the word to the top of the "last 10" column.</summary>
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

    /// <summary>Undo rolls back only this user's marks.</summary>
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

    [Fact]
    public async Task Mark_without_a_scope_records_no_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now);

        Assert.Empty(db.SortingVisits);
    }

    [Fact]
    public async Task Mark_with_a_chapter_scope_records_the_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now, new SortingScope(DictionaryId, ChapterId: 1));

        var visit = Assert.Single(db.SortingVisits);
        Assert.Equal(UserId, visit.UserId);
        Assert.Equal(DictionaryId, visit.DictionaryId);
        Assert.Equal(1, visit.ChapterId);
        Assert.Equal(Now, visit.LastSortedAt);
    }

    /// <summary>
    /// The point of upserting rather than appending: sorting a chapter for an hour leaves one
    /// row that says "last touched a minute ago", not one row per word marked.
    /// </summary>
    [Fact]
    public async Task Marking_again_in_one_scope_moves_the_same_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);
        var scope = new SortingScope(DictionaryId, ChapterId: 1);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now, scope);
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Unknown, Now.AddMinutes(5), scope);

        var visit = Assert.Single(db.SortingVisits);
        Assert.Equal(Now.AddMinutes(5), visit.LastSortedAt);
    }

    /// <summary>The whole book is its own scope, and one row of it — not a null-chapter row per mark.</summary>
    [Fact]
    public async Task The_whole_book_scope_is_one_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);
        var book = new SortingScope(DictionaryId, ChapterId: null);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now, book);
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Known, Now.AddMinutes(1), book);

        var visit = Assert.Single(db.SortingVisits);
        Assert.Null(visit.ChapterId);
        Assert.Equal(Now.AddMinutes(1), visit.LastSortedAt);
    }

    [Fact]
    public async Task Chapter_and_whole_book_visits_are_separate_rows()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now, new SortingScope(DictionaryId, ChapterId: 1));
        await service.MarkAsync(UserId, wordPairId: 2, SortStatus.Known, Now.AddMinutes(1), new SortingScope(DictionaryId, ChapterId: null));
        await service.MarkAsync(UserId, wordPairId: 4, SortStatus.Known, Now.AddMinutes(2), new SortingScope(DictionaryId, ChapterId: 2));

        Assert.Equal([null, 1L, 2L], db.SortingVisits.Select(v => v.ChapterId).OrderBy(id => id).ToList());
    }

    /// <summary>
    /// A repeated mark returns early — the word is already on that shelf — but the user is
    /// still sitting in the scope, so the visit must move anyway.
    /// </summary>
    [Fact]
    public async Task A_repeated_mark_still_moves_the_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);
        var scope = new SortingScope(DictionaryId, ChapterId: 1);

        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now, scope);
        await service.MarkAsync(UserId, wordPairId: 1, SortStatus.Known, Now.AddMinutes(3), scope);

        var visit = Assert.Single(db.SortingVisits);
        Assert.Equal(Now.AddMinutes(3), visit.LastSortedAt);
    }

    /// <summary>A refused mark is not a visit: nothing was sorted.</summary>
    [Fact]
    public async Task A_refused_mark_records_no_visit()
    {
        await using var db = await ArrangeAsync();
        var service = new WordSortingService(db);

        var marked = await service.MarkAsync(
            UserId, wordPairId: 999, SortStatus.Known, Now, new SortingScope(DictionaryId, ChapterId: 1));

        Assert.False(marked);
        Assert.Empty(db.SortingVisits);
    }

    /// <summary>
    /// A scope naming a chapter of another book is a client bug, and the visit would send the
    /// user somewhere they never were — the mark stands, the visit is dropped.
    /// </summary>
    [Fact]
    public async Task A_chapter_outside_the_named_book_records_no_visit()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 99, Name = "Other", WordsCount = 0 });
        await db.SaveChangesAsync();

        var service = new WordSortingService(db);

        var marked = await service.MarkAsync(
            UserId, wordPairId: 1, SortStatus.Known, Now, new SortingScope(DictionaryId: 99, ChapterId: 1));

        Assert.True(marked);
        Assert.Empty(db.SortingVisits);
    }
}
