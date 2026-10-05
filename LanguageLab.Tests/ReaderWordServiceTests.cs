using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ReaderWordServiceTests
{
    private const long User = 1;
    private const long Other = 2;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTranslator(string? answer) : ITranslator
    {
        public Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken) => Task.FromResult(answer);
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

        var adjust = TestWords.Pair(1, "adjust", "налаштувати");
        var orphan = TestWords.Pair(2, "orphan", null);
        var silo = TestWords.Pair(3, "silo", "силос");
        var cold = TestWords.Pair(4, "cold", "холодний");
        var hidden = TestWords.Pair(5, "hidden", "прихований");
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

    private static ReaderWordService Service(
        ApplicationDbContext db, string? providerAnswer = null, UncachedTranslationLimiter? limiter = null) =>
        new(
            db,
            new TranslationService(
                db, new FakeTranslator(providerAnswer), limiter ?? new UncachedTranslationLimiter(TimeProvider.System),
                FakeEnglishLexicon.Knowing("adjust", "orphan", "silo", "cold", "hidden", "frank", "waif")),
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

        Assert.Equal(expected, await Service(db).LearnTargetAsync(User, UserRole.User, "uk", lemma, dictionaryId));
    }

    /// <summary>A word translated only in uk is out of reach for a pl learner, even in its own book.</summary>
    [Fact]
    public async Task A_book_word_without_a_translation_in_the_language_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(LearnTarget.Personal, await Service(db).LearnTargetAsync(User, UserRole.User, "pl", "adjust", 10L));
    }

    [Fact]
    public async Task The_panel_gets_translation_status_and_learn_target()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db).GetAsync(User, UserRole.User, LearnerLanguages.Default, "adjust", 10, CancellationToken.None);

        Assert.Equal(new ReaderWordView("adjust", "налаштувати", TranslationSource.Dictionary, ReaderWordShelf.New, false, LearnTarget.Book), view);
    }

    /// <summary>A word the provider just translated is in no book: it can only go to My words.</summary>
    [Fact]
    public async Task A_word_new_to_the_vocabulary_is_translated_and_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db, "бездомна дитина").GetAsync(User, UserRole.User, LearnerLanguages.Default, "waif", 10, CancellationToken.None);

        Assert.Equal("бездомна дитина", view.Translation);
        Assert.Equal(LearnTarget.Personal, view.LearnTarget);
    }

    [Fact]
    public async Task Learn_puts_a_word_of_this_book_on_dont_know()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "uk", "adjust", 10, null, Now);

        Assert.Equal(LearnOutcome.Shelved, outcome);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == User && u.WordPairId == 1));
    }

    [Fact]
    public async Task Learn_moves_a_known_word_of_this_book_back_to_dont_know()
    {
        await using var db = await ArrangeAsync();

        await Service(db).LearnAsync(User, UserRole.User, "uk", "silo", 10, null, Now);

        Assert.False(await db.KnownWords.AnyAsync(k => k.WordPairId == 3));
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == 3));
    }

    /// <summary>Reading Wool, a word that is only in Dune must not be shelved in Dune.</summary>
    [Fact]
    public async Task Learn_of_a_word_from_another_book_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "uk", "cold", 10, null, Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.False(await db.UnknownWords.AnyAsync(u => u.WordPairId == 4));
        var mine = await db.Words.SingleAsync(w => w.Word == "cold" && w.OwnerId == User);
        Assert.Equal("холодний", (await db.WordTranslations.SingleAsync(t => t.WordPairId == mine.Id && t.Language == "uk")).Text);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == mine.Id));
    }

    [Fact]
    public async Task Learn_while_reading_a_book_without_a_dictionary_goes_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "uk", "adjust", null, null, Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        var mine = await db.Words.SingleAsync(w => w.Word == "adjust" && w.OwnerId == User);
        Assert.Equal("налаштувати", (await db.WordTranslations.SingleAsync(t => t.WordPairId == mine.Id && t.Language == "uk")).Text);
    }

    /// <summary>A typed translation never goes into a shared row: it lands in the user's own list.</summary>
    [Fact]
    public async Task Learn_without_a_shared_translation_uses_the_typed_one()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, UserRole.User, "uk", "orphan", 10, " сирота ", Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.False(await db.WordTranslations.AnyAsync(t => t.WordPairId == 2));
        var mine = await db.Words.SingleAsync(w => w.Word == "orphan" && w.OwnerId == User);
        Assert.Equal("сирота", (await db.WordTranslations.SingleAsync(t => t.WordPairId == mine.Id && t.Language == "uk")).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Learn_without_any_translation_is_refused(string? typed)
    {
        await using var db = await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).LearnAsync(User, UserRole.User, "uk", "orphan", 10, typed, Now));
    }

    [Fact]
    public async Task Learning_the_same_personal_word_twice_is_harmless()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.LearnAsync(User, UserRole.User, "uk", "orphan", null, "сирота", Now);
        var again = await service.LearnAsync(User, UserRole.User, "uk", "orphan", null, "сирота", Now);

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
        Assert.False(await db.WordTranslations.AnyAsync(t => t.WordPairId == row.Id));
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

    /// <summary>The panel's undo: offered for a shelved word, withheld once the word is in training.</summary>
    [Fact]
    public async Task A_shelved_word_can_be_reset_but_one_in_training_cannot()
    {
        await using var db = await ArrangeAsync();

        var shelved = await Service(db).GetAsync(User, UserRole.User, LearnerLanguages.Default, "silo", 10, CancellationToken.None);

        Assert.Equal(ReaderWordShelf.Known, shelved.Shelf);
        Assert.True(shelved.CanReset);

        db.WordProgresses.Add(new WordProgress { UserId = User, WordPairId = 3, Box = 2 });
        await db.SaveChangesAsync();

        Assert.False((await Service(db).GetAsync(User, UserRole.User, LearnerLanguages.Default, "silo", 10, CancellationToken.None)).CanReset);
    }

    [Fact]
    public async Task Reset_takes_a_known_word_off_the_shelf()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).ResetAsync(User, "silo");

        Assert.Equal(ResetOutcome.Cleared, outcome);
        Assert.Equal(ReaderWordShelf.New, (await new ReaderWordStatusService(db).GetShelfAsync(User, "silo")).Shelf);
    }

    [Fact]
    public async Task Reset_takes_an_ignored_word_off_the_shelf()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.IgnoreAsync(User, "frank", Now);

        await service.ResetAsync(User, "frank");

        Assert.False(await db.ExcludedWords.AnyAsync(e => e.UserId == User));
    }

    [Fact]
    public async Task Reset_takes_a_word_of_this_book_off_dont_know()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.LearnAsync(User, UserRole.User, "uk", "adjust", 10, null, Now);

        await service.ResetAsync(User, "adjust");

        Assert.False(await db.UnknownWords.AnyAsync(u => u.UserId == User && u.WordPairId == 1));
    }

    /// <summary>Undoing "Add to training" has to take the word out of My words, or it keeps being trained.</summary>
    [Fact]
    public async Task Reset_removes_a_word_that_went_to_my_words()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);
        await service.LearnAsync(User, UserRole.User, "uk", "cold", 10, null, Now);

        await service.ResetAsync(User, "cold");

        Assert.False(await db.Words.AnyAsync(w => w.Word == "cold" && w.OwnerId == User));
        Assert.False(await db.UnknownWords.AnyAsync(u => u.UserId == User));
    }

    [Fact]
    public async Task Reset_is_refused_for_a_word_already_in_training()
    {
        await using var db = await ArrangeAsync();
        db.WordProgresses.Add(new WordProgress { UserId = User, WordPairId = 3, Box = 2 });
        await db.SaveChangesAsync();

        var outcome = await Service(db).ResetAsync(User, "silo");

        Assert.Equal(ResetOutcome.InTraining, outcome);
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == User && k.WordPairId == 3));
    }

    [Fact]
    public async Task Resetting_a_word_on_no_shelf_is_harmless()
    {
        await using var db = await ArrangeAsync();

        Assert.Equal(ResetOutcome.Cleared, await Service(db).ResetAsync(User, "adjust"));
    }

    [Fact]
    public async Task Reset_leaves_another_learners_shelf_alone()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { UserId = Other, WordPairId = 1, CreatedAt = Now });
        await db.SaveChangesAsync();

        await Service(db).ResetAsync(User, "adjust");

        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == Other && k.WordPairId == 1));
    }

    [Fact]
    public async Task A_rate_limited_lookup_still_answers_the_panel_without_a_translation()
    {
        await using var db = await ArrangeAsync();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-29T12:00:00Z"));
        var service = Service(db, "сирота", new UncachedTranslationLimiter(clock));

        await service.GetAsync(User, UserRole.User, LearnerLanguages.Default, "orphan", 10, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(3));
        var view = await service.GetAsync(User, UserRole.User, LearnerLanguages.Default, "waif", 10, CancellationToken.None);

        Assert.Equal(
            new ReaderWordView("waif", null, TranslationSource.RateLimited, ReaderWordShelf.New, false, LearnTarget.Personal, 7),
            view);
    }
}
