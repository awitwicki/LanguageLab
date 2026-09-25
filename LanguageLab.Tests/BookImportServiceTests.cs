using LanguageLab.Application.Services;
using LanguageLab.Domain;
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

        var result = await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

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

        var result = await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);

        // abide, silo, cleaning — three unique words despite four chapter rows.
        Assert.Equal(3, dictionary.WordsCount);
        Assert.Equal(3, result.TotalWords);
    }

    [Fact]
    public async Task Chapters_keep_their_order_and_own_word_counts()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

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
        await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

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
        var result = await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

        Assert.Equal(1, await db.Words.CountAsync(w => w.Word == "silo"));
        Assert.Equal(2, result.NewWords);      // abide, cleaning
        Assert.Equal(1, result.ReusedWords);   // silo
    }

    [Fact]
    public async Task Reimporting_the_same_book_does_not_duplicate_word_pairs()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);
        await service.ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

        Assert.Equal(3, await db.Words.CountAsync());
        Assert.Equal(2, await db.Dictionaries.CountAsync());
    }

    [Fact]
    public async Task Flat_import_creates_no_chapters()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var result = await service.ImportAsync(
            new ImportRequest(
                Name: "Top 500",
                Chapters: null,
                Words: [new ImportWord("the", 100), new ImportWord("been", 90)]),
            ownerId: 1,
            status: PublicationStatus.Published);

        Assert.Empty(await db.Chapters.Where(c => c.DictionaryId == result.DictionaryId).ToListAsync());
        Assert.Equal(2, result.TotalWords);
    }

    [Fact]
    public async Task Import_stamps_the_owner_and_the_requested_visibility()
    {
        await using var db = NewContext();

        var result = await new BookImportService(db).ImportAsync(
            new ImportRequest("Wool", null, [new ImportWord("abide", 3)]),
            ownerId: 42,
            status: PublicationStatus.Private);

        var dictionary = await db.Dictionaries.FirstAsync(d => d.Id == result.DictionaryId);

        Assert.Equal(42, dictionary.OwnerId);
        Assert.Equal(PublicationStatus.Private, dictionary.PublicationStatus);
    }

    /// <summary>
    /// A personal word spelled like a book word is neither reused nor a collision: the book gets
    /// its own shared row. Without the owner filter the lookup's ToDictionary would throw on it.
    /// </summary>
    [Fact]
    public async Task An_owned_word_with_the_same_spelling_is_ignored_by_import()
    {
        await using var db = NewContext();
        db.Users.Add(new TelegramUser { Id = 5, TelegramUserId = 555 });
        db.Words.Add(new WordPair { Id = 1, Word = "silo", Translation = "силос", OwnerId = 5 });
        await db.SaveChangesAsync();

        var result = await new BookImportService(db).ImportAsync(TwoChapterBook(), ownerId: 1, status: PublicationStatus.Published);

        var silos = await db.Words.Where(w => w.Word == "silo").OrderBy(w => w.Id).ToListAsync();
        Assert.Equal(2, silos.Count);
        Assert.Null(silos[1].OwnerId);
        Assert.Equal("", silos[1].Translation);
        Assert.Equal(3, result.NewWords);
    }

    [Fact]
    public async Task The_file_hash_is_kept_lowercase()
    {
        await using var db = NewContext();
        var hash = new string('a', 64);

        // The caller must have opened this exact file before the hash is trusted; the
        // ReaderBook row proves that, and it is stored already-normalized (lowercase).
        db.ReaderBooks.Add(new ReaderBook
        {
            UserId = 1, FileHash = hash, Title = "Wool", ChaptersCount = 2,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await new BookImportService(db).ImportAsync(
            TwoChapterBook() with { FileHash = new string('A', 64) }, ownerId: 1, status: PublicationStatus.Published);

        Assert.Equal(hash, (await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId)).FileHash);
    }

    [Fact]
    public async Task A_malformed_file_hash_is_dropped()
    {
        await using var db = NewContext();

        var result = await new BookImportService(db).ImportAsync(
            TwoChapterBook() with { FileHash = "nope" }, ownerId: 1, status: PublicationStatus.Published);

        Assert.Null((await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId)).FileHash);
    }

    [Fact]
    public async Task Unusable_words_are_dropped_and_counted()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var request = new ImportRequest(
            Name: "Wool",
            Chapters: [new ImportChapter(0, "Chapter 1",
            [
                new ImportWord("silo", 3),
                new ImportWord("abide", 2),
                new ImportWord("cleaning", 2),
                new ImportWord("gather", 1),
                new ImportWord("слово", 9),
            ])],
            Words: null);

        var result = await service.ImportAsync(request, ownerId: 1, status: PublicationStatus.Published);

        Assert.Equal(4, result.TotalWords);
        Assert.Equal(1, result.DroppedWords);
        Assert.False(await db.Words.AnyAsync(w => w.Word == "слово"));
    }

    // Review Focus 1: every chapter empties out, so the pre-existing "import is empty" guard
    // would fire first and blame the wrong thing.
    [Fact]
    public async Task A_book_that_is_mostly_junk_is_refused_with_the_junk_message()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var request = new ImportRequest(
            Name: "Junk",
            Chapters: [new ImportChapter(0, "Chapter 1",
            [
                new ImportWord("🙂🙂🙂", 1),
                new ImportWord("слово", 1),
                new ImportWord("ещё", 1),
                new ImportWord("silo", 1),
            ])],
            Words: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync(request, ownerId: 1, status: PublicationStatus.Published));

        Assert.Contains("not a book", error.Message);
    }

    // Review Focus 2: nothing usable at all must not divide by zero.
    [Fact]
    public async Task A_book_with_no_usable_words_at_all_is_refused_without_dividing_by_zero()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var request = new ImportRequest(
            Name: "Junk",
            Chapters: [new ImportChapter(0, "Chapter 1", [new ImportWord("слово", 1)])],
            Words: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync(request, ownerId: 1, status: PublicationStatus.Published));

        Assert.Contains("not a book", error.Message);
    }

    [Fact]
    public async Task Too_many_words_are_refused()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        // Distinct, all-letter spellings: ImportWordText.IsValid rejects digits, so a plain
        // "word{i}" scheme (as digits) would be dropped as junk rather than exercise the count guard.
        var words = Enumerable.Range(0, BookImportService.MaxWords + 1)
            .Select(i => new ImportWord($"word{FourLetterSuffix(i)}", 1))
            .ToList();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync(new ImportRequest("Big", null, words), ownerId: 1, status: PublicationStatus.Published));

        Assert.Contains("too many words", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A four-letter, base-26 (a-z) encoding of <paramref name="value"/>, unique per input in [0, 26^4).</summary>
    private static string FourLetterSuffix(int value)
    {
        var chars = new char[4];

        for (var i = chars.Length - 1; i >= 0; i--)
        {
            chars[i] = (char)('a' + value % 26);
            value /= 26;
        }

        return new string(chars);
    }

    [Fact]
    public async Task Too_many_chapters_are_refused()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var chapters = Enumerable.Range(0, BookImportService.MaxChapters + 1)
            .Select(i => new ImportChapter(i, $"Chapter {i}", [new ImportWord("silo", 1)]))
            .ToList();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync(new ImportRequest("Big", chapters, null), ownerId: 1, status: PublicationStatus.Published));

        Assert.Contains("too many chapters", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_file_hash_the_caller_has_never_opened_is_dropped()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var request = TwoChapterBook() with { FileHash = new string('a', 64) };
        var result = await service.ImportAsync(request, ownerId: 1, status: PublicationStatus.Published);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        Assert.Null(dictionary.FileHash);
    }

    [Fact]
    public async Task A_file_hash_from_the_callers_own_library_is_kept()
    {
        await using var db = NewContext();
        var hash = new string('a', 64);

        db.ReaderBooks.Add(new ReaderBook
        {
            UserId = 1, FileHash = hash, Title = "Wool", ChaptersCount = 2,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new BookImportService(db);
        var result = await service.ImportAsync(TwoChapterBook() with { FileHash = hash }, ownerId: 1, status: PublicationStatus.Published);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        Assert.Equal(hash, dictionary.FileHash);
    }

    [Fact]
    public async Task Another_users_library_does_not_vouch_for_a_hash()
    {
        await using var db = NewContext();
        var hash = new string('a', 64);

        db.ReaderBooks.Add(new ReaderBook
        {
            UserId = 2, FileHash = hash, Title = "Wool", ChaptersCount = 2,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new BookImportService(db);
        var result = await service.ImportAsync(TwoChapterBook() with { FileHash = hash }, ownerId: 1, status: PublicationStatus.Published);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        Assert.Null(dictionary.FileHash);
    }

    [Fact]
    public async Task A_long_name_and_chapter_title_are_cut_not_refused()
    {
        await using var db = NewContext();
        var service = new BookImportService(db);

        var request = new ImportRequest(
            Name: new string('n', TitleText.MaxLength + 40),
            Chapters: [new ImportChapter(0, new string('t', TitleText.MaxLength + 40), [new ImportWord("silo", 1)])],
            Words: null);

        var result = await service.ImportAsync(request, ownerId: 1, status: PublicationStatus.Published);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        var chapter = await db.Chapters.SingleAsync(c => c.DictionaryId == result.DictionaryId);

        Assert.Equal(TitleText.MaxLength, dictionary.Name.Length);
        Assert.Equal(TitleText.MaxLength, chapter.Title.Length);
    }

    [Theory]
    [InlineData(UserRole.User, false, PublicationStatus.Private)]
    [InlineData(UserRole.User, true, PublicationStatus.Pending)]
    [InlineData(UserRole.Uploader, false, PublicationStatus.Private)]
    [InlineData(UserRole.Uploader, true, PublicationStatus.Published)]
    [InlineData(UserRole.Admin, true, PublicationStatus.Published)]
    public void An_import_never_publishes_on_a_plain_users_say_so(
        UserRole role, bool requested, PublicationStatus expected) =>
        Assert.Equal(expected, BookImportService.StatusFor(role, requested));
}
