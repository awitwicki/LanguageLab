using System.Security.Cryptography;
using System.Text;
using LanguageLab.Application.Books;
using LanguageLab.Application.Import;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Books;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests.Import;

public class BookFileImportServiceTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>Identity lemmas for every listed word — enough to make a test book "English".</summary>
    private static FakeEnglishLexicon Lexicon(params string[] lemmas) =>
        new(lemmas.ToDictionary(lemma => lemma, lemma => lemma, StringComparer.Ordinal));

    /// <summary>The C2 reader fixture's content words, so BookFixtures.ReaderBookXml imports clean.</summary>
    private static readonly string[] ReaderBookWords =
    [
        "compared", "beginning", "fewer", "individuals", "emerging", "still", "formed", "stratum",
        "difficulty", "reintegrating", "silo", "quiet", "cold", "men", "tried", "adjust",
    ];

    private static (BookFileImportService Service, FakeTranslationQueue Queue) Make(
        ApplicationDbContext db, FakeEnglishLexicon lexicon) =>
        Make(db, lexicon, new ImportQuota(TimeProvider.System));

    private static (BookFileImportService Service, FakeTranslationQueue Queue) Make(
        ApplicationDbContext db, FakeEnglishLexicon lexicon, ImportQuota quota)
    {
        var queue = new FakeTranslationQueue();
        var service = new BookFileImportService(new BookParser(), lexicon, new BookImportService(db), queue, quota);

        return (service, queue);
    }

    // default(ChapterMode) is Depth null — exactly ChapterMode.Leaf.
    private static Task<ImportResult> Import(
        BookFileImportService service, byte[] file, string fileName = "book.fb2",
        ChapterMode mode = default, bool requestPublication = false, LearnerLanguage? language = null,
        UserRole role = UserRole.User) =>
        service.ImportAsync(
            file, fileName, mode, requestPublication, ownerId: 1, role, language, CancellationToken.None);

    private const string FrenchFb2 = """
        <FictionBook><description><title-info><book-title>Jardin</book-title></title-info></description>
        <body><section><title><p>Un</p></title>
        <p>les enfants jouaient dans le jardin toute la journee ensemble</p>
        </section></body></FictionBook>
        """;

    private const string CyrillicFb2 = """
        <FictionBook><description><title-info><book-title>Слово</book-title></title-info></description>
        <body><section><title><p>Один</p></title><p>Привіт світе як справи сьогодні</p></section></body></FictionBook>
        """;

    [Fact]
    public async Task Reads_a_plain_fb2_and_stores_the_server_computed_hash()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon(ReaderBookWords));
        var bytes = BookFixtures.Utf8(BookFixtures.ReaderBookXml);

        var result = await Import(service, bytes);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        Assert.Equal("Death's End", dictionary.Name);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), dictionary.FileHash);
        // Leaf mode: The Swordholder + Year 62 (the notes body stays out — C2's parser).
        Assert.Equal(2, await db.Chapters.CountAsync(c => c.DictionaryId == result.DictionaryId));
    }

    [Fact]
    public async Task Falls_back_to_the_file_name_when_the_book_names_none()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon("silo"));
        var fb2 = "<FictionBook><body><section><title><p>One</p></title><p>silo silo silo</p></section></body></FictionBook>";

        var result = await Import(service, BookFixtures.Utf8(fb2), fileName: "Deaths-End.fb2.zip");

        Assert.Equal("Deaths-End", (await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId)).Name);
    }

    [Theory]
    [InlineData("Wool.FB2", "Wool")]
    [InlineData("wool.epub.ZIP", "wool")]
    [InlineData("wool.epub", "wool")]
    [InlineData("notes.txt", "notes.txt")]
    public void Strips_the_book_extension_single_or_double_whatever_its_case(string fileName, string title)
    {
        Assert.Equal(title, BookFileImportService.TitleFromFileName(fileName));
    }

    [Fact]
    public async Task Cuts_chapters_at_the_requested_depth()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon(ReaderBookWords));

        var result = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), mode: new ChapterMode(1));

        var chapter = await db.Chapters.SingleAsync(c => c.DictionaryId == result.DictionaryId);
        Assert.Equal("Part One", chapter.Title);
    }

    // Review Focus 4: a depth past the book's deepest level must not crash or lose text.
    [Fact]
    public async Task A_depth_past_the_books_deepest_level_still_yields_every_leaf()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon(ReaderBookWords));

        var result = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), mode: new ChapterMode(9));

        Assert.Equal(2, await db.Chapters.CountAsync(c => c.DictionaryId == result.DictionaryId));
    }

    // Review Focus 4: depth zero makes every top-level section one chapter.
    [Fact]
    public async Task Depth_zero_makes_every_top_level_section_a_chapter()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon(ReaderBookWords));

        var result = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), mode: new ChapterMode(0));

        var chapter = await db.Chapters.SingleAsync(c => c.DictionaryId == result.DictionaryId);
        Assert.Equal("Part One", chapter.Title);
    }

    [Fact]
    public async Task Enqueues_translation_for_the_importers_language()
    {
        await using var db = NewContext();
        var (service, queue) = Make(db, Lexicon(ReaderBookWords));

        var result = await Import(
            service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), language: LearnerLanguages.Find("uk"));

        Assert.True(result.TranslationQueued);
        Assert.Equal((result.DictionaryId, "uk"), Assert.Single(queue.Enqueued));
    }

    [Fact]
    public async Task Skips_the_queue_without_a_language()
    {
        await using var db = NewContext();
        var (service, queue) = Make(db, Lexicon(ReaderBookWords));

        var result = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), language: null);

        Assert.False(result.TranslationQueued);
        Assert.Empty(queue.Enqueued);
    }

    // Review Focus 2: Latin-script non-English refuses cleanly — and leaves nothing behind.
    [Fact]
    public async Task Refuses_a_book_that_is_not_english()
    {
        await using var db = NewContext();
        var (service, queue) = Make(db, Lexicon("silo"));

        await Assert.ThrowsAsync<NotEnglishBookException>(
            () => Import(service, BookFixtures.Utf8(FrenchFb2), language: LearnerLanguages.Find("uk")));

        Assert.Empty(await db.Dictionaries.ToListAsync());
        Assert.Empty(queue.Enqueued);
    }

    // Review Focus 3: a Cyrillic book is "not English", never "the import is empty".
    [Fact]
    public async Task A_cyrillic_book_is_refused_as_not_english()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon("silo"));

        await Assert.ThrowsAsync<NotEnglishBookException>(() => Import(service, BookFixtures.Utf8(CyrillicFb2)));
    }

    // Review Focus 1: names dominate distinct words but not occurrences.
    [Fact]
    public async Task An_english_book_dense_with_names_still_imports()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon("silo", "quiet", "climbed", "top"));
        var fb2 = """
            <FictionBook><body><section><title><p>One</p></title>
            <p>the silo was quiet and holston climbed to the top of hogwarts</p>
            </section></body></FictionBook>
            """;

        var result = await Import(service, BookFixtures.Utf8(fb2));

        Assert.Equal(4, result.TotalWords);
    }

    // Review Focus 5: the parser's own refusals keep their kind — never swallowed by the gate.
    [Fact]
    public async Task Drm_stays_encrypted_and_garbage_stays_invalid()
    {
        await using var db = NewContext();
        var (service, _) = Make(db, Lexicon("silo"));
        const string drm = """
            <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <EncryptedData xmlns="http://www.w3.org/2001/04/xmlenc#">
                <EncryptionMethod Algorithm="http://www.w3.org/2001/04/xmlenc#aes256-cbc"/>
                <CipherData><CipherReference URI="OEBPS/text/ch01.xhtml"/></CipherData>
              </EncryptedData>
            </encryption>
            """;

        var encrypted = await Assert.ThrowsAsync<BookFormatException>(
            () => Import(service, BookFixtures.Epub3Bytes(("META-INF/encryption.xml", drm))));
        var invalid = await Assert.ThrowsAsync<BookFormatException>(
            () => Import(service, Encoding.UTF8.GetBytes("<not a book")));

        Assert.Equal(BookFormatError.Encrypted, encrypted.Error);
        Assert.Equal(BookFormatError.Invalid, invalid.Error);
    }

    [Fact]
    public async Task Reads_an_epub_end_to_end()
    {
        await using var db = NewContext();
        var (service, queue) = Make(db, Lexicon(
            "compared", "beginning", "fewer", "individuals", "emerging", "formed", "stratum",
            "men", "tried", "adjust", "droplet", "came", "noon"));

        var result = await Import(
            service, BookFixtures.Epub3Bytes(), fileName: "deaths-end.epub", language: LearnerLanguages.Find("pl"));

        var dictionary = await db.Dictionaries.SingleAsync(d => d.Id == result.DictionaryId);
        Assert.Equal("Death's End", dictionary.Name);
        Assert.Equal(3, await db.Chapters.CountAsync(c => c.DictionaryId == result.DictionaryId));
        Assert.Equal((result.DictionaryId, "pl"), Assert.Single(queue.Enqueued));
    }

    // Review Focus: a failed import must not spend the day's slot.
    [Fact]
    public async Task A_refused_book_does_not_spend_the_quota()
    {
        await using var db = NewContext();
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var (service, _) = Make(db, Lexicon(ReaderBookWords), quota);

        await Assert.ThrowsAsync<NotEnglishBookException>(() => Import(service, BookFixtures.Utf8(FrenchFb2)));

        // Immediately retryable: the refusal above did not spend the day's slot.
        var result = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml));

        Assert.True(result.DictionaryId > 0);
    }

    [Fact]
    public async Task A_second_import_today_is_refused_after_a_successful_one()
    {
        await using var db = NewContext();
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var (service, _) = Make(db, Lexicon(ReaderBookWords), quota);

        await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml));

        var refused = await Assert.ThrowsAsync<ImportQuotaExceededException>(
            () => Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml)));

        Assert.Equal(ImportQuota.Window, refused.RetryAfter);
    }

    // Review Focus: an admin stays exempt even after a success of their own, not only before it.
    [Fact]
    public async Task An_admin_bypasses_the_quota_even_after_a_success()
    {
        await using var db = NewContext();
        var quota = new ImportQuota(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var (service, _) = Make(db, Lexicon(ReaderBookWords), quota);

        await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), role: UserRole.Admin);

        var second = await Import(service, BookFixtures.Utf8(BookFixtures.ReaderBookXml), role: UserRole.Admin);

        Assert.True(second.DictionaryId > 0);
    }
}
