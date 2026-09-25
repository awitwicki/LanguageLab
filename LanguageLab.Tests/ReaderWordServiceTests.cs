using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ReaderWordServiceTests
{
    private const long User = 1;
    private const long Other = 2;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTranslator(string? answer) : ITranslator
    {
        public Task<string?> TranslateAsync(string word, CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    /// <summary>
    /// Wool (10, public) holds adjust and silo; Dune (20, public) holds cold; Secret (30, Other's,
    /// private) holds hidden. orphan is shared but untranslated and in no book. silo is on User's
    /// "know" shelf.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(new TelegramUser { Id = User, TelegramUserId = 11 }, new TelegramUser { Id = Other, TelegramUserId = 22 });

        var adjust = new WordPair { Id = 1, Word = "adjust", Translation = "налаштувати" };
        var orphan = new WordPair { Id = 2, Word = "orphan", Translation = "" };
        var silo = new WordPair { Id = 3, Word = "silo", Translation = "силос" };
        var cold = new WordPair { Id = 4, Word = "cold", Translation = "холодний" };
        var hidden = new WordPair { Id = 5, Word = "hidden", Translation = "прихований" };
        db.Words.AddRange(adjust, orphan, silo, cold, hidden);

        var wool = new Domain.Entities.Dictionary
        { Id = 10, Name = "Wool", WordsCount = 2, PublicationStatus = PublicationStatus.Published };
        wool.Words = [adjust, silo];
        var dune = new Domain.Entities.Dictionary
        { Id = 20, Name = "Dune", WordsCount = 1, PublicationStatus = PublicationStatus.Published };
        dune.Words = [cold];
        var secret = new Domain.Entities.Dictionary { Id = 30, Name = "Secret", OwnerId = Other, PublicationStatus = PublicationStatus.Private, WordsCount = 1 };
        secret.Words = [hidden];
        db.Dictionaries.AddRange(wool, dune, secret);

        db.KnownWords.Add(new KnownWord { UserId = User, WordPairId = 3, CreatedAt = Now });

        await db.SaveChangesAsync();
        return db;
    }

    private static ReaderWordService Service(ApplicationDbContext db, string? providerAnswer = null) =>
        new(
            db,
            new TranslationService(db, new FakeTranslator(providerAnswer)),
            new ReaderWordStatusService(db),
            new WordSortingService(db),
            new PersonalDictionaryService(db, new WordSelectionService(db), new LearningProgressService(db)),
            new DictionaryAccessService(db));

    [Theory]
    [InlineData("adjust", 10L, LearnTarget.Book)]
    [InlineData("cold", 10L, LearnTarget.Personal)]
    [InlineData("cold", null, LearnTarget.Personal)]
    [InlineData("orphan", 10L, LearnTarget.Personal)]
    [InlineData("hidden", 30L, LearnTarget.Personal)]
    public async Task The_learn_target_is_this_books_dictionary_or_my_words(string lemma, long? dictionaryId, LearnTarget expected)
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(expected, await Service(db).LearnTargetAsync(User, UserRole.User, lemma, dictionaryId));
    }

    [Fact]
    public async Task The_panel_gets_translation_status_and_learn_target()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db).GetAsync(User, UserRole.User, "adjust", 10, CancellationToken.None);

        Assert.Equal(new ReaderWordView("adjust", "налаштувати", TranslationSource.Dictionary, ReaderWordStatus.New, LearnTarget.Book), view);
    }

    /// <summary>A word the provider just translated is in no book: it can only go to My words.</summary>
    [Fact]
    public async Task A_word_new_to_the_vocabulary_is_translated_and_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db, "бездомна дитина").GetAsync(User, UserRole.User, "waif", 10, CancellationToken.None);

        Assert.Equal("бездомна дитина", view.Translation);
        Assert.Equal(LearnTarget.Personal, view.LearnTarget);
    }

    [Fact]
    public async Task Learn_puts_a_word_of_this_book_on_dont_know()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "adjust", 10, null, Now);

        Assert.Equal(LearnOutcome.Shelved, outcome);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == User && u.WordPairId == 1));
    }

    [Fact]
    public async Task Learn_moves_a_known_word_of_this_book_back_to_dont_know()
    {
        await using var db = await ArrangeAsync();

        await Service(db).LearnAsync(User, UserRole.User, "silo", 10, null, Now);

        Assert.False(await db.KnownWords.AnyAsync(k => k.WordPairId == 3));
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == 3));
    }

    /// <summary>Reading Wool, a word that is only in Dune must not be shelved in Dune.</summary>
    [Fact]
    public async Task Learn_of_a_word_from_another_book_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "cold", 10, null, Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.False(await db.UnknownWords.AnyAsync(u => u.WordPairId == 4));
        var mine = await db.Words.SingleAsync(w => w.Word == "cold" && w.OwnerId == User);
        Assert.Equal("холодний", mine.Translation);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == mine.Id));
    }

    [Fact]
    public async Task Learn_while_reading_a_book_without_a_dictionary_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "adjust", null, null, Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.Equal("налаштувати", (await db.Words.SingleAsync(w => w.Word == "adjust" && w.OwnerId == User)).Translation);
    }

    /// <summary>A typed translation never goes into a shared row: it lands in the user's own list.</summary>
    [Fact]
    public async Task Learn_without_a_shared_translation_uses_the_typed_one()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "orphan", 10, " сирота ", Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.Equal("", (await db.Words.SingleAsync(w => w.Id == 2)).Translation);
        Assert.Equal("сирота", (await db.Words.SingleAsync(w => w.Word == "orphan" && w.OwnerId == User)).Translation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Learn_without_any_translation_is_refused(string? typed)
    {
        await using var db = await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).LearnAsync(User, UserRole.User, "orphan", 10, typed, Now));
    }

    [Fact]
    public async Task Learning_the_same_personal_word_twice_is_harmless()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.LearnAsync(User, UserRole.User, "orphan", null, "сирота", Now);
        var again = await service.LearnAsync(User, UserRole.User, "orphan", null, "сирота", Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, again);
        Assert.Equal(1, await db.Words.CountAsync(w => w.Word == "orphan" && w.OwnerId == User));
    }

    [Fact]
    public async Task Known_shelves_a_shared_word_even_without_a_translation()
    {
        await using var db = await ArrangeAsync();

        await Service(db).MarkKnownAsync(User, "orphan", Now);

        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == User && k.WordPairId == 2));
    }

    [Fact]
    public async Task Known_creates_the_shared_row_when_there_is_none()
    {
        await using var db = await ArrangeAsync();

        await Service(db).MarkKnownAsync(User, "waif", Now);

        var row = await db.Words.SingleAsync(w => w.Word == "waif");
        Assert.Null(row.OwnerId);
        Assert.Equal("", row.Translation);
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == User && k.WordPairId == row.Id));
    }

    /// <summary>Names are what Ignore is for — and the provider echoes them back, so there is often no row.</summary>
    [Fact]
    public async Task Ignore_excludes_a_name_and_it_stops_being_highlighted()
    {
        await using var db = await ArrangeAsync();

        await Service(db).IgnoreAsync(User, "frank", Now);

        var row = await db.Words.SingleAsync(w => w.Word == "frank");
        Assert.True(await db.ExcludedWords.AnyAsync(e => e.UserId == User && e.WordPairId == row.Id));
        Assert.Equal(ReaderWordStatus.Known, await new ReaderWordStatusService(db).GetAsync(User, "frank"));
    }

    [Fact]
    public async Task Ignoring_twice_is_harmless()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.IgnoreAsync(User, "frank", Now);
        await service.IgnoreAsync(User, "frank", Now);

        Assert.Equal(1, await db.Words.CountAsync(w => w.Word == "frank"));
        Assert.Equal(1, await db.ExcludedWords.CountAsync(e => e.UserId == User));
    }
}
