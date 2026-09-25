using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ChapterStatsServiceTests
{
    private const long UserId = 1;
    private const long OtherId = 2;
    private const long DictionaryId = 10;
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A three-chapter book; user 1 has starred the middle chapter, user 2 nothing.</summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = UserId, TelegramUserId = 11 },
            new TelegramUser { Id = OtherId, TelegramUserId = 22 });

        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = DictionaryId, Name = "Wool", WordsCount = 0 });

        db.Chapters.AddRange(
            new Chapter { Id = 1, DictionaryId = DictionaryId, Order = 0, Title = "One", WordsCount = 3 },
            new Chapter { Id = 2, DictionaryId = DictionaryId, Order = 1, Title = "Two", WordsCount = 2 },
            new Chapter { Id = 3, DictionaryId = DictionaryId, Order = 2, Title = "", WordsCount = 1 });

        db.StarredChapters.Add(new StarredChapter { Id = 1, UserId = UserId, ChapterId = 2, CreatedAt = Now });

        await db.SaveChangesAsync();

        return db;
    }

    private static ChapterStatsService Service(ApplicationDbContext db) =>
        new(db, new WordSortingService(db), new WordSelectionService(db), new LearningProgressService(db));

    [Fact]
    public async Task Every_chapter_in_book_order_when_nothing_is_filtered()
    {
        await using var db = await ArrangeAsync();

        var views = await Service(db).GetChapterViewsAsync(UserId, DictionaryId, Now);

        Assert.Equal(new long[] { 1, 2, 3 }, views.Select(v => v.Id));
        Assert.Equal(new[] { 0, 1, 2 }, views.Select(v => v.Order));
        Assert.Equal(new[] { "One", "Two", "" }, views.Select(v => v.Title));
        Assert.Equal(new[] { 3, 2, 1 }, views.Select(v => v.WordsCount));
    }

    [Fact]
    public async Task Only_the_requested_chapters_still_in_book_order()
    {
        await using var db = await ArrangeAsync();

        var views = await Service(db).GetChapterViewsAsync(UserId, DictionaryId, Now, [3, 1]);

        Assert.Equal(new long[] { 1, 3 }, views.Select(v => v.Id));
    }

    [Fact]
    public async Task IsStarred_reflects_the_callers_own_stars()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        var mine = await service.GetChapterViewsAsync(UserId, DictionaryId, Now);
        var theirs = await service.GetChapterViewsAsync(OtherId, DictionaryId, Now);

        Assert.Equal(new[] { false, true, false }, mine.Select(v => v.IsStarred));
        Assert.All(theirs, v => Assert.False(v.IsStarred));
    }

    [Fact]
    public async Task A_book_without_chapters_gives_an_empty_list()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 20, Name = "Flat", WordsCount = 0 });
        await db.SaveChangesAsync();

        var views = await Service(db).GetChapterViewsAsync(UserId, 20, Now);

        Assert.Empty(views);
    }
}
