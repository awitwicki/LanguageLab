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
}
