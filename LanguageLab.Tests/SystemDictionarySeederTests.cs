using LanguageLab.Application.Seeding;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class SystemDictionarySeederTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Seeds_the_irregular_verbs_dictionary_with_four_groups()
    {
        await using var db = NewContext();
        var seeder = new SystemDictionarySeeder(db);

        await seeder.SeedAsync();

        var dictionary = await db.Dictionaries
            .Include(d => d.Chapters)
            .SingleAsync(d => d.Name == IrregularVerbs.DictionaryName);

        Assert.Null(dictionary.OwnerId);
        Assert.True(dictionary.IsPublic);
        Assert.Equal(68, dictionary.WordsCount);

        var chapters = dictionary.Chapters.OrderBy(c => c.Order).ToList();
        Assert.Equal([0, 1, 2, 3], chapters.Select(c => c.Order));
        Assert.Equal([9, 3, 29, 27], chapters.Select(c => c.WordsCount));
        Assert.Equal(68, await db.DictionaryWords.CountAsync(dw => dw.DictionaryId == dictionary.Id));
    }

    [Fact]
    public async Task Second_run_adds_nothing()
    {
        await using var db = NewContext();
        var seeder = new SystemDictionarySeeder(db);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(1, await db.Dictionaries.CountAsync());
        Assert.Equal(4, await db.Chapters.CountAsync());
        Assert.Equal(68, await db.Words.CountAsync());
    }

    [Fact]
    public async Task Reuses_an_existing_word_and_fills_only_an_empty_translation()
    {
        await using var db = NewContext();
        db.Words.Add(new WordPair { Word = "cut – cut – cut", Translation = "" });
        db.Words.Add(new WordPair { Word = "put – put – put", Translation = "класти (своє)" });
        await db.SaveChangesAsync();
        var seeder = new SystemDictionarySeeder(db);

        await seeder.SeedAsync();

        Assert.Equal(68, await db.Words.CountAsync());
        Assert.Equal("різати", (await db.Words.SingleAsync(w => w.Word == "cut – cut – cut")).Translation);
        Assert.Equal("класти (своє)", (await db.Words.SingleAsync(w => w.Word == "put – put – put")).Translation);

        var dictionary = await db.Dictionaries.SingleAsync();
        Assert.Equal(68, await db.DictionaryWords.CountAsync(dw => dw.DictionaryId == dictionary.Id));
    }

    [Fact]
    public async Task Frequency_follows_the_table_order_so_batches_keep_the_families_together()
    {
        await using var db = NewContext();
        var seeder = new SystemDictionarySeeder(db);

        await seeder.SeedAsync();

        // Batches take the most frequent words first: the chapter scope by ChapterWord.Count,
        // the whole dictionary by DictionaryWord.Frequency. Both must reproduce the table.
        var groupThree = await db.Chapters.SingleAsync(c => c.Order == 2);
        var chapterOrder = await db.ChapterWords
            .Where(cw => cw.ChapterId == groupThree.Id)
            .OrderByDescending(cw => cw.Count)
            .ThenBy(cw => cw.WordPair.Word)
            .Select(cw => cw.WordPair.Word)
            .Take(3)
            .ToListAsync();

        Assert.Equal(["buy – bought – bought", "bring – brought – brought", "think – thought – thought"], chapterOrder);

        var dictionaryOrder = await db.DictionaryWords
            .OrderByDescending(dw => dw.Frequency)
            .ThenBy(dw => dw.WordPair.Word)
            .Select(dw => dw.WordPair.Word)
            .ToListAsync();

        Assert.Equal("cut – cut – cut", dictionaryOrder.First());
        Assert.Equal("draw – drew – drawn", dictionaryOrder.Last());
        Assert.Equal(IrregularVerbs.All.Select(v => v.Word), dictionaryOrder);
    }
}
