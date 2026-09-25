using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ReaderWordStatusServiceTests
{
    private const long User = 1;
    private const long Other = 2;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// adjust — in Leitner, not learned; abide — "don't know" shelf; silo — "know" shelf;
    /// cold — learned in Leitner, its "don't know" row still there; run — shared on "know",
    /// personal on "don't know"; holston — excluded; ghost — only Other's shelf.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(new TelegramUser { Id = User, TelegramUserId = 11 }, new TelegramUser { Id = Other, TelegramUserId = 22 });

        db.Words.AddRange(
            new WordPair { Id = 1, Word = "adjust", Translation = "налаштувати" },
            new WordPair { Id = 2, Word = "abide", Translation = "дотримуватися" },
            new WordPair { Id = 3, Word = "silo", Translation = "силос" },
            new WordPair { Id = 4, Word = "cold", Translation = "холодний" },
            new WordPair { Id = 5, Word = "run", Translation = "бігти" },
            new WordPair { Id = 6, Word = "run", Translation = "запускати", OwnerId = User },
            new WordPair { Id = 7, Word = "ghost", Translation = "привид" },
            new WordPair { Id = 8, Word = "holston", Translation = "" });

        db.WordProgresses.AddRange(
            new WordProgress { UserId = User, WordPairId = 1, Box = 2, IsLearned = false },
            new WordProgress { UserId = User, WordPairId = 4, Box = 5, IsLearned = true });

        db.UnknownWords.AddRange(
            new UnknownWord { UserId = User, WordPairId = 2, CreatedAt = Now },
            new UnknownWord { UserId = User, WordPairId = 4, CreatedAt = Now },
            new UnknownWord { UserId = User, WordPairId = 6, CreatedAt = Now },
            new UnknownWord { UserId = Other, WordPairId = 7, CreatedAt = Now });

        db.KnownWords.AddRange(
            new KnownWord { UserId = User, WordPairId = 3, CreatedAt = Now },
            new KnownWord { UserId = User, WordPairId = 5, CreatedAt = Now });

        db.ExcludedWords.Add(new ExcludedWord { UserId = User, WordPairId = 8, CreatedAt = Now });

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Every_word_with_a_standing_is_listed_by_status()
    {
        await using var db = await ArrangeAsync();

        var statuses = await new ReaderWordStatusService(db).GetAsync(User);

        Assert.Equal(["abide", "adjust", "run"], statuses.Learning);
        Assert.Equal(["cold", "holston", "silo"], statuses.Known);
    }

    [Theory]
    [InlineData("adjust", ReaderWordStatus.Learning)]
    [InlineData("cold", ReaderWordStatus.Known)]
    [InlineData("run", ReaderWordStatus.Learning)]
    [InlineData("ghost", ReaderWordStatus.New)]
    [InlineData("nothing", ReaderWordStatus.New)]
    public async Task One_word_has_one_status(string word, ReaderWordStatus expected)
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(expected, await new ReaderWordStatusService(db).GetAsync(User, word));
    }

    /// <summary>
    /// The word panel needs the shelf itself, not the highlight status: an ignored word is a
    /// word the learner threw away, a known one is a word they already have.
    /// </summary>
    [Theory]
    [InlineData("abide", ReaderWordShelf.Learning)]
    [InlineData("silo", ReaderWordShelf.Known)]
    [InlineData("holston", ReaderWordShelf.Ignored)]
    [InlineData("run", ReaderWordShelf.Learning)]
    [InlineData("ghost", ReaderWordShelf.New)]
    [InlineData("nothing", ReaderWordShelf.New)]
    public async Task One_word_sits_on_one_shelf(string word, ReaderWordShelf expected)
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(expected, (await new ReaderWordStatusService(db).GetShelfAsync(User, word)).Shelf);
    }

    /// <summary>What the panel's undo is guarded by: a Leitner row means the word is really being trained.</summary>
    [Theory]
    [InlineData("adjust", true)]
    [InlineData("cold", true)]
    [InlineData("abide", false)]
    [InlineData("holston", false)]
    [InlineData("nothing", false)]
    public async Task A_word_with_a_leitner_row_is_in_training(string word, bool expected)
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(expected, (await new ReaderWordStatusService(db).GetShelfAsync(User, word)).InTraining);
    }
}
