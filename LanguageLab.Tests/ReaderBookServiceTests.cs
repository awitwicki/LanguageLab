using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ReaderBookServiceTests
{
    private const long Reader = 1;
    private const long Other = 2;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Wool = new('a', 64);
    private static readonly string Hidden = new('b', 64);
    private static readonly string Dune = new('c', 64);

    /// <summary>"Wool" is a public dictionary imported from file Wool; "Hidden" is Other's private one from file Hidden.</summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = Reader, TelegramUserId = 11 },
            new TelegramUser { Id = Other, TelegramUserId = 22 });

        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 10, Name = "Wool", IsPublic = true, FileHash = Wool },
            new Domain.Entities.Dictionary { Id = 20, Name = "Hidden", OwnerId = Other, IsPublic = false, FileHash = Hidden });

        await db.SaveChangesAsync();
        return db;
    }

    private static ReaderBookService Service(ApplicationDbContext db) => new(db, new DictionaryAccessService(db));

    private static ReaderPosition At(int chapter, double progress) => new(chapter, 1, 2, progress);

    [Fact]
    public async Task Registering_twice_keeps_one_row_and_the_position()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "Hugh Howey", 30, Now);
        await service.SavePositionAsync(Reader, Wool, At(7, 0.25), Now.AddMinutes(1), Now.AddMinutes(1));
        var again = await service.RegisterAsync(Reader, UserRole.User, Wool, " Wool (2nd ed.) ", "", 31, Now.AddMinutes(2));

        Assert.Equal(1, await db.ReaderBooks.CountAsync());
        Assert.Equal("Wool (2nd ed.)", again.Title);
        Assert.Equal(31, again.ChaptersCount);
        Assert.Equal(7, again.ChapterIndex);
        Assert.Equal(0.25, again.Progress);
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("  ", 3)]
    [InlineData("Wool", 0)]
    public async Task Register_refuses_an_empty_title_or_no_chapters(string title, int chapters)
    {
        await using var db = await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(db).RegisterAsync(Reader, UserRole.User, Wool, title, "", chapters, Now));
    }

    [Fact]
    public async Task The_library_is_the_callers_books_last_read_first()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);
        await service.RegisterAsync(Reader, UserRole.User, Dune, "Dune", "", 48, Now);
        await service.RegisterAsync(Other, UserRole.User, Hidden, "Theirs", "", 5, Now);
        await service.SavePositionAsync(Reader, Dune, At(1, 0.1), Now.AddHours(1), Now.AddHours(1));

        var books = await service.ListAsync(Reader, UserRole.User);

        Assert.Equal([Dune, Wool], books.Select(b => b.FileHash));
    }

    [Fact]
    public async Task A_visible_dictionary_from_the_same_file_is_linked()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);

        Assert.Equal(10, view.DictionaryId);
    }

    [Fact]
    public async Task A_dictionary_the_caller_cannot_see_is_not_linked()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, Hidden, "Hidden", "", 3, Now);

        Assert.Null(view.DictionaryId);
    }

    /// <summary>Imported before Dictionary.FileHash existed, so reopening the book must not duplicate it.</summary>
    [Fact]
    public async Task A_legacy_dictionary_with_no_file_hash_is_linked_by_the_books_title()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 30, Name = "Legacy Book", IsPublic = true, FileHash = null });
        await db.SaveChangesAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, new('d', 64), "Legacy Book", "", 3, Now);

        Assert.Equal(30, view.DictionaryId);
    }

    [Fact]
    public async Task A_same_titled_dictionary_with_a_different_file_hash_is_not_linked_by_name()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 31, Name = "Legacy Book", IsPublic = true, FileHash = new('e', 64) });
        await db.SaveChangesAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, new('d', 64), "Legacy Book", "", 3, Now);

        Assert.Null(view.DictionaryId);
    }

    [Fact]
    public async Task A_file_hash_match_wins_over_a_name_match()
    {
        await using var db = await ArrangeAsync();
        var hash = new string('d', 64);
        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary { Id = 32, Name = "Something else", IsPublic = true, FileHash = hash },
            new Domain.Entities.Dictionary { Id = 33, Name = "Legacy Book", IsPublic = true, FileHash = null });
        await db.SaveChangesAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, hash, "Legacy Book", "", 3, Now);

        Assert.Equal(32, view.DictionaryId);
    }

    [Fact]
    public async Task An_invisible_legacy_dictionary_is_not_linked_by_name()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = 34, Name = "Their Legacy Book", OwnerId = Other, IsPublic = false, FileHash = null,
        });
        await db.SaveChangesAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, new('d', 64), "Their Legacy Book", "", 3, Now);

        Assert.Null(view.DictionaryId);
    }

    [Fact]
    public async Task A_personal_dictionary_is_never_linked_by_name()
    {
        await using var db = await ArrangeAsync();
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = 35, Name = "My words", OwnerId = Reader, IsPersonal = true, IsPublic = false, FileHash = null,
        });
        await db.SaveChangesAsync();

        var view = await Service(db).RegisterAsync(Reader, UserRole.User, new('d', 64), "My words", "", 3, Now);

        Assert.Null(view.DictionaryId);
    }

    [Fact]
    public async Task Saving_moves_the_position_and_clamps_it()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);

        var saved = await service.SavePositionAsync(Reader, Wool, new ReaderPosition(3, -1, 4, 1.7), Now.AddMinutes(5), Now.AddMinutes(5));

        var book = await db.ReaderBooks.SingleAsync();
        Assert.True(saved);
        Assert.Equal((3, 0, 4, 1.0), (book.ChapterIndex, book.ParagraphIndex, book.SentenceIndex, book.Progress));
        Assert.Equal(Now.AddMinutes(5), book.UpdatedAt);
    }

    /// <summary>A phone that was offline syncs an old position late: it must not drag the reader back.</summary>
    [Fact]
    public async Task An_older_position_does_not_overwrite_a_newer_one()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);

        await service.SavePositionAsync(Reader, Wool, At(9, 0.3), Now.AddMinutes(10), Now.AddMinutes(10));
        var accepted = await service.SavePositionAsync(Reader, Wool, At(2, 0.05), Now.AddMinutes(3), Now.AddMinutes(11));

        Assert.True(accepted);
        Assert.Equal(9, (await db.ReaderBooks.SingleAsync()).ChapterIndex);
    }

    [Fact]
    public async Task A_client_clock_ahead_of_the_server_is_capped_at_now()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);

        await service.SavePositionAsync(Reader, Wool, At(4, 0.1), Now.AddDays(3), Now.AddMinutes(1));

        Assert.Equal(Now.AddMinutes(1), (await db.ReaderBooks.SingleAsync()).UpdatedAt);
    }

    [Fact]
    public async Task Someone_elses_book_or_an_unregistered_one_is_refused()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.RegisterAsync(Other, UserRole.User, Hidden, "Theirs", "", 5, Now);

        Assert.False(await service.SavePositionAsync(Reader, Hidden, At(1, 0.1), Now, Now));
        Assert.False(await service.SavePositionAsync(Reader, Dune, At(1, 0.1), Now, Now));
        Assert.False(await service.RemoveAsync(Reader, Hidden));
    }

    [Fact]
    public async Task Removing_deletes_the_row()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.RegisterAsync(Reader, UserRole.User, Wool, "Wool", "", 30, Now);

        Assert.True(await service.RemoveAsync(Reader, Wool));
        Assert.Empty(await service.ListAsync(Reader, UserRole.User));
    }
}
