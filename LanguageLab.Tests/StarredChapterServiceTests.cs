using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class StarredChapterServiceTests
{
    private const long Owner = 1;
    private const long Stranger = 2;
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Three books: "Wool" (public, chapters 1–2), "Private" (the owner's, not public,
    /// chapter 4) and "Dune" (system, chapter 5). Nothing starred yet.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = Owner, TelegramUserId = 11 },
            new TelegramUser { Id = Stranger, TelegramUserId = 22 });

        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 10, Name = "Wool", OwnerId = Owner, IsPublic = true },
            new Domain.Entities.Dictionary { Id = 20, Name = "Private", OwnerId = Owner, IsPublic = false },
            new Domain.Entities.Dictionary { Id = 30, Name = "Dune", OwnerId = null, IsPublic = true });

        db.Chapters.AddRange(
            new Chapter { Id = 1, DictionaryId = 10, Order = 0, Title = "Holston", WordsCount = 3 },
            new Chapter { Id = 2, DictionaryId = 10, Order = 1, Title = "Juliette", WordsCount = 2 },
            new Chapter { Id = 4, DictionaryId = 20, Order = 0, Title = "Secret", WordsCount = 1 },
            new Chapter { Id = 5, DictionaryId = 30, Order = 0, Title = "Arrakis", WordsCount = 1 });

        await db.SaveChangesAsync();

        return db;
    }

    private static StarredChapterService Service(ApplicationDbContext db) =>
        new(
            db,
            new DictionaryAccessService(db),
            new ChapterStatsService(db, new WordSortingService(db), new WordSelectionService(db), new LearningProgressService(db)));

    [Fact]
    public async Task Starring_twice_leaves_one_row_and_still_succeeds()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        Assert.True(await service.StarAsync(Owner, UserRole.User, 1, Now));
        Assert.True(await service.StarAsync(Owner, UserRole.User, 1, Now.AddMinutes(1)));

        var stars = await db.StarredChapters.ToListAsync();
        Assert.Single(stars);
        Assert.Equal(Owner, stars[0].UserId);
        Assert.Equal(1, stars[0].ChapterId);
        Assert.Equal(Now, stars[0].CreatedAt);
    }

    [Fact]
    public async Task A_chapter_of_an_invisible_dictionary_cannot_be_starred()
    {
        await using var db = await ArrangeAsync();

        Assert.False(await Service(db).StarAsync(Stranger, UserRole.User, 4, Now));
        Assert.Empty(await db.StarredChapters.ToListAsync());
    }

    [Fact]
    public async Task An_admin_may_star_someone_elses_private_chapter()
    {
        await using var db = await ArrangeAsync();

        Assert.True(await Service(db).StarAsync(Stranger, UserRole.Admin, 4, Now));
        Assert.Single(await db.StarredChapters.ToListAsync());
    }

    [Fact]
    public async Task An_unknown_chapter_cannot_be_starred()
    {
        await using var db = await ArrangeAsync();

        Assert.False(await Service(db).StarAsync(Owner, UserRole.User, 999, Now));
    }

    [Fact]
    public async Task Unstarring_reports_whether_there_was_a_star()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        Assert.False(await service.UnstarAsync(Owner, 1));

        await service.StarAsync(Owner, UserRole.User, 1, Now);

        Assert.True(await service.UnstarAsync(Owner, 1));
        Assert.Empty(await db.StarredChapters.ToListAsync());
    }

    [Fact]
    public async Task Unstarring_touches_only_the_callers_star()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.StarAsync(Owner, UserRole.User, 1, Now);
        await service.StarAsync(Stranger, UserRole.User, 1, Now);

        Assert.True(await service.UnstarAsync(Stranger, 1));

        var left = await db.StarredChapters.SingleAsync();
        Assert.Equal(Owner, left.UserId);
    }

    [Fact]
    public async Task The_list_is_ordered_by_book_name_then_chapter_order()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.StarAsync(Owner, UserRole.User, 2, Now);
        await service.StarAsync(Owner, UserRole.User, 5, Now);
        await service.StarAsync(Owner, UserRole.User, 4, Now);
        await service.StarAsync(Owner, UserRole.User, 1, Now);

        var list = await service.GetStarredAsync(Owner, UserRole.User, Now);

        Assert.Equal(
            new[] { ("Dune", 5L), ("Private", 4L), ("Wool", 1L), ("Wool", 2L) },
            list.Select(s => (s.DictionaryName, s.Chapter.Id)));
        Assert.Equal(new long[] { 30, 20, 10, 10 }, list.Select(s => s.DictionaryId));
        Assert.All(list, s => Assert.True(s.Chapter.IsStarred));
        Assert.Equal("Holston", list[2].Chapter.Title);
        Assert.Equal(3, list[2].Chapter.WordsCount);
    }

    [Fact]
    public async Task A_star_on_a_book_that_turned_private_is_hidden_but_can_still_be_removed()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.StarAsync(Stranger, UserRole.User, 1, Now);

        (await db.Dictionaries.SingleAsync(d => d.Id == 10)).IsPublic = false;
        await db.SaveChangesAsync();

        Assert.Empty(await service.GetStarredAsync(Stranger, UserRole.User, Now));
        Assert.True(await service.UnstarAsync(Stranger, 1));
    }

    [Fact]
    public async Task Nothing_starred_gives_an_empty_list()
    {
        await using var db = await ArrangeAsync();

        Assert.Empty(await Service(db).GetStarredAsync(Owner, UserRole.User, Now));
    }
}
