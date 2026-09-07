using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class BookImportServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ImportRequest TwoChapterBook() =>
        new(
            Name: "Wool",
            Chapters:
            [
                new ImportChapter(0, "Chapter 1", [new ImportWord("abide", 3), new ImportWord("silo", 10)]),
                new ImportChapter(1, "Chapter 2", [new ImportWord("silo", 5), new ImportWord("cleaning", 2)])
            ],
            Words: null);

    [Fact]
    public async Task Frequency_is_the_sum_across_chapters()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(TwoChapterBook());

        var silo = await db.Words.SingleAsync(w => w.Word == "silo");
        var frequency = await db.DictionaryWords
            .Where(dw => dw.DictionaryId == result.DictionaryId && dw.WordPairId == silo.Id)
            .Select(dw => dw.Frequency)
            .SingleAsync();

        Assert.Equal(15, frequency);
    }

    [Fact]
    public async Task Dictionary_words_count_is_unique_words_not_chapter_sum()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(TwoChapterBook());

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);

        // abide, silo, cleaning — три унікальні, попри чотири рядки по главах.
        Assert.Equal(3, dictionary.WordsCount);
        Assert.Equal(3, result.TotalWords);
    }

    [Fact]
    public async Task Chapters_keep_their_order_and_own_word_counts()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(TwoChapterBook());

        var chapters = await db.Chapters
            .Where(c => c.DictionaryId == result.DictionaryId)
            .OrderBy(c => c.Order)
            .ToListAsync();

        Assert.Equal(["Chapter 1", "Chapter 2"], chapters.Select(c => c.Title));
        Assert.Equal([2, 2], chapters.Select(c => c.WordsCount));
    }

    [Fact]
    public async Task Existing_translation_is_never_overwritten()
    {
        await using var db = NewContext();
        db.Words.Add(new WordPair { Id = 1, Word = "silo", Translation = "бункер" });
        await db.SaveChangesAsync();

        var service = new BookImportService(db);
        await service.ImportAsync(TwoChapterBook());

        var silo = await db.Words.SingleAsync(w => w.Word == "silo");

        Assert.Equal("бункер", silo.Translation);
    }

    [Fact]
    public async Task Existing_word_is_reused_not_duplicated()
    {
        await using var db = NewContext();
        db.Words.Add(new WordPair { Id = 1, Word = "silo", Translation = "бункер" });
        await db.SaveChangesAsync();

        var service = new BookImportService(db);
        var result = await service.ImportAsync(TwoChapterBook());

        Assert.Equal(1, await db.Words.CountAsync(w => w.Word == "silo"));
        Assert.Equal(2, result.NewWords);      // abide, cleaning
        Assert.Equal(1, result.ReusedWords);   // silo
    }

    [Fact]
    public async Task Reimporting_the_same_book_does_not_duplicate_word_pairs()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        await service.ImportAsync(TwoChapterBook());
        await service.ImportAsync(TwoChapterBook());

        Assert.Equal(3, await db.Words.CountAsync());
        Assert.Equal(2, await db.Dictionaries.CountAsync());
    }

    [Fact]
    public async Task Flat_import_creates_no_chapters()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(new ImportRequest(
            Name: "Top 500",
            Chapters: null,
            Words: [new ImportWord("the", 100), new ImportWord("be", 90)]));

        Assert.Empty(await db.Chapters.Where(c => c.DictionaryId == result.DictionaryId).ToListAsync());
        Assert.Equal(2, result.TotalWords);
    }
}
