using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class SchemaTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Join-таблиця стала сутністю з навантаженням, але скіп-навігація має вціліти:
    /// на неї спирається LearnableQuery і кнопки бота.
    /// </summary>
    [Fact]
    public async Task Skip_navigation_survives_payload_join()
    {
        await using var db = NewContext();

        var dictionary = new Domain.Entities.Dictionary { Id = 1, Name = "book", WordsCount = 1 };
        var word = new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" };
        dictionary.Words = [word];

        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        var found = await db.Words.CountAsync(w => w.Dictionaries.Any(d => d.Id == 1));

        Assert.Equal(1, found);
    }

    /// <summary>Частота живе на join-рядку, а не на слові: одне слово має різну частоту в різних книжках.</summary>
    [Fact]
    public async Task Frequency_is_stored_per_dictionary()
    {
        await using var db = NewContext();

        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 47 });
        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = 2, WordPairId = 1, Frequency = 3 });
        await db.SaveChangesAsync();

        var frequencies = await db.DictionaryWords
            .Where(dw => dw.WordPairId == 1)
            .OrderBy(dw => dw.DictionaryId)
            .Select(dw => dw.Frequency)
            .ToListAsync();

        Assert.Equal([47, 3], frequencies);
    }

    /// <summary>Глави прив'язані до словника й нумеруються з нуля.</summary>
    [Fact]
    public async Task Chapters_belong_to_dictionary()
    {
        await using var db = NewContext();

        var dictionary = new Domain.Entities.Dictionary { Id = 1, Name = "book", WordsCount = 0 };
        dictionary.Chapters =
        [
            new Chapter { Id = 1, Order = 0, Title = "Chapter 1", WordsCount = 10 },
            new Chapter { Id = 2, Order = 1, Title = "Chapter 2", WordsCount = 12 }
        ];

        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        var titles = await db.Chapters
            .Where(c => c.DictionaryId == 1)
            .OrderBy(c => c.Order)
            .Select(c => c.Title)
            .ToListAsync();

        Assert.Equal(["Chapter 1", "Chapter 2"], titles);
    }

    /// <summary>Deleting a user takes their shelves and progress with them: no orphan rows keyed to a gone user.</summary>
    [Fact]
    public async Task Deleting_a_user_removes_their_shelves_and_progress()
    {
        await using var db = NewContext();

        var user = new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = DateTime.UtcNow };
        var word = new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" };

        db.Users.Add(user);
        db.Words.Add(word);
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = 1, WordPairId = 1, CreatedAt = DateTime.UtcNow });
        db.WordProgresses.Add(new WordProgress { Id = 1, UserId = 1, WordPairId = 1, Box = 1 });
        await db.SaveChangesAsync();

        // The in-memory provider cascades through the change tracker, so the dependents
        // must be loaded for the cascade to be observable — the relational provider
        // does the same work in the database via ON DELETE CASCADE.
        await db.KnownWords.ToListAsync();
        await db.WordProgresses.ToListAsync();

        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == 1));
        await db.SaveChangesAsync();

        Assert.Empty(await db.KnownWords.ToListAsync());
        Assert.Empty(await db.WordProgresses.ToListAsync());
        Assert.Single(await db.Words.ToListAsync());
    }

    /// <summary>A deleted user must not take their dictionaries with them: the book survives, ownerless.</summary>
    [Fact]
    public async Task Deleting_a_user_keeps_their_dictionaries_and_clears_the_owner()
    {
        await using var db = NewContext();

        db.Users.Add(new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = DateTime.UtcNow });
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = 1, Name = "Wool", WordsCount = 0, OwnerId = 1, IsPublic = true,
        });
        await db.SaveChangesAsync();

        await db.Dictionaries.ToListAsync();

        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == 1));
        await db.SaveChangesAsync();

        var dictionary = await db.Dictionaries.FirstAsync(d => d.Id == 1);

        Assert.Null(dictionary.OwnerId);
        Assert.True(dictionary.IsPublic);
    }

    /// <summary>Login upserts by TelegramUserId, so the column must not allow a second row with the same id.</summary>
    [Fact]
    public void Telegram_user_id_is_unique()
    {
        using var db = NewContext();

        var index = db.Model
            .FindEntityType(typeof(TelegramUser))!
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(TelegramUser.TelegramUserId)));

        Assert.True(index.IsUnique);
    }
}
