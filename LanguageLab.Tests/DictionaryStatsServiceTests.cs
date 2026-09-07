using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class DictionaryStatsServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Словник 1 («Wool»): silo(15), holston(7), abide(3), cleaning(2), jahns(1).
    /// Словник 2 («Other»): dune(100) — має не протікати в статистику першого.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = NewContext();

        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 1, Name = "Wool", WordsCount = 5 });
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 2, Name = "Other", WordsCount = 1 });

        db.Words.AddRange(
            new WordPair { Id = 1, Word = "silo", Translation = "" },
            new WordPair { Id = 2, Word = "abide", Translation = "" },
            new WordPair { Id = 3, Word = "cleaning", Translation = "" },
            new WordPair { Id = 4, Word = "holston", Translation = "" },
            new WordPair { Id = 5, Word = "jahns", Translation = "" },
            new WordPair { Id = 6, Word = "dune", Translation = "" });

        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 15 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 2, Frequency = 3 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 3, Frequency = 2 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 4, Frequency = 7 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 5, Frequency = 1 },
            new DictionaryWord { DictionaryId = 2, WordPairId = 6, Frequency = 100 });

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task TopWords_sorted_by_frequency_desc_and_scoped_to_dictionary()
    {
        await using var db = await ArrangeAsync();
        var service = new DictionaryStatsService(db);

        var top = await service.GetTopWordsAsync(1, take: 3);

        Assert.Equal(new[] { "silo", "holston", "abide" }, top.Select(t => t.Word));
        Assert.Equal(new[] { 15, 7, 3 }, top.Select(t => t.Frequency));
        Assert.Equal(new long[] { 1, 4, 2 }, top.Select(t => t.WordPairId));
    }

    [Fact]
    public async Task TopWords_default_take_returns_everything_when_dictionary_is_small()
    {
        await using var db = await ArrangeAsync();
        var service = new DictionaryStatsService(db);

        var top = await service.GetTopWordsAsync(1);

        Assert.Equal(5, top.Count);
        Assert.DoesNotContain(top, t => t.Word == "dune");
    }

    [Fact]
    public async Task TopWords_take_below_one_is_clamped_to_one()
    {
        await using var db = await ArrangeAsync();
        var service = new DictionaryStatsService(db);

        var top = await service.GetTopWordsAsync(1, take: 0);

        Assert.Single(top);
        Assert.Equal("silo", top[0].Word);
    }

    [Fact]
    public async Task TopWords_ties_are_ordered_by_word()
    {
        await using var db = NewContext();
        db.Dictionaries.Add(new Domain.Entities.Dictionary { Id = 1, Name = "Ties", WordsCount = 2 });
        db.Words.AddRange(
            new WordPair { Id = 1, Word = "zebra", Translation = "" },
            new WordPair { Id = 2, Word = "apple", Translation = "" });
        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 4 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 2, Frequency = 4 });
        await db.SaveChangesAsync();

        var top = await new DictionaryStatsService(db).GetTopWordsAsync(1);

        Assert.Equal(new[] { "apple", "zebra" }, top.Select(t => t.Word));
    }
}
