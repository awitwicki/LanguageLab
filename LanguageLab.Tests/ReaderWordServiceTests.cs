using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class ReaderWordServiceTests
{
    private const long User = 1;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTranslator(string? answer) : ITranslator
    {
        public Task<string?> TranslateAsync(string word, CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    /// <summary>
    /// adjust — shared, translated, in a dictionary (so Learn can shelve it); orphan — shared,
    /// untranslated, in no dictionary; silo — shared, translated, in the same dictionary, on the
    /// "know" shelf; cold — shared, translated, in NO dictionary (Learn must fall back to My words).
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.Add(new TelegramUser { Id = User, TelegramUserId = 11 });
        var adjust = new WordPair { Id = 1, Word = "adjust", Translation = "налаштувати" };
        var orphan = new WordPair { Id = 2, Word = "orphan", Translation = "" };
        var silo = new WordPair { Id = 3, Word = "silo", Translation = "силос" };
        var cold = new WordPair { Id = 4, Word = "cold", Translation = "холодний" };
        db.Words.AddRange(adjust, orphan, silo, cold);

        var dictionary = new Domain.Entities.Dictionary { Id = 10, Name = "Wool", WordsCount = 2 };
        dictionary.Words = [adjust, silo];
        db.Dictionaries.Add(dictionary);

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
            new PersonalDictionaryService(db, new WordSelectionService(db), new LearningProgressService(db)));

    [Fact]
    public async Task The_panel_gets_translation_status_and_shared_presence()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db).GetAsync(User, "adjust", CancellationToken.None);

        Assert.Equal(new ReaderWordView("adjust", "налаштувати", TranslationSource.Dictionary, ReaderWordStatus.New, true), view);
    }

    /// <summary>The provider's answer is cached into the shared vocabulary, so the word is shelvable at once.</summary>
    [Fact]
    public async Task A_word_new_to_the_vocabulary_becomes_shared_on_lookup()
    {
        await using var db = await ArrangeAsync();

        var view = await Service(db, "бездомна дитина").GetAsync(User, "waif", CancellationToken.None);

        Assert.Equal("бездомна дитина", view.Translation);
        Assert.True(view.InSharedVocabulary);
    }

    [Fact]
    public async Task Learn_puts_a_translated_shared_word_on_dont_know()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, "adjust", null, Now);

        Assert.Equal(LearnOutcome.Shelved, outcome);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == User && u.WordPairId == 1));
    }

    [Fact]
    public async Task Learn_moves_a_known_word_back_to_dont_know()
    {
        await using var db = await ArrangeAsync();

        await Service(db).LearnAsync(User, "silo", null, Now);

        Assert.False(await db.KnownWords.AnyAsync(k => k.WordPairId == 3));
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == 3));
    }

    /// <summary>A typed translation never goes into a shared row: it lands in the user's own list.</summary>
    [Fact]
    public async Task Learn_without_a_shared_translation_adds_to_my_words()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, "orphan", " сирота ", Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.Equal("", (await db.Words.SingleAsync(w => w.Id == 2)).Translation);
        var mine = await db.Words.SingleAsync(w => w.Word == "orphan" && w.OwnerId == User);
        Assert.Equal("сирота", mine.Translation);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == mine.Id));
    }

    /// <summary>A shared, translated word that belongs to no dictionary would sit on the shelf
    /// forever and never enter a batch — it goes to "My words" instead, using the translation
    /// already there, with no typed input required.</summary>
    [Fact]
    public async Task Learn_without_a_dictionary_link_falls_back_to_my_words_using_the_shared_translation()
    {
        await using var db = await ArrangeAsync();

        var outcome = await Service(db).LearnAsync(User, "cold", null, Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, outcome);
        Assert.Equal("холодний", (await db.Words.SingleAsync(w => w.Id == 4)).Translation);
        var mine = await db.Words.SingleAsync(w => w.Word == "cold" && w.OwnerId == User);
        Assert.Equal("холодний", mine.Translation);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.WordPairId == mine.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Learn_without_any_translation_is_refused(string? typed)
    {
        await using var db = await ArrangeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).LearnAsync(User, "orphan", typed, Now));
    }

    [Fact]
    public async Task Learning_the_same_personal_word_twice_is_harmless()
    {
        await using var db = await ArrangeAsync();
        var service = Service(db);

        await service.LearnAsync(User, "orphan", "сирота", Now);
        var again = await service.LearnAsync(User, "orphan", "сирота", Now);

        Assert.Equal(LearnOutcome.AddedToPersonal, again);
        Assert.Equal(1, await db.Words.CountAsync(w => w.Word == "orphan" && w.OwnerId == User));
    }

    [Fact]
    public async Task Known_shelves_a_shared_word_even_without_a_translation()
    {
        await using var db = await ArrangeAsync();

        Assert.True(await Service(db).MarkKnownAsync(User, "orphan", Now));
        Assert.True(await db.KnownWords.AnyAsync(k => k.UserId == User && k.WordPairId == 2));
    }

    [Fact]
    public async Task Known_for_a_word_outside_the_shared_vocabulary_is_refused()
    {
        await using var db = await ArrangeAsync();

        Assert.False(await Service(db).MarkKnownAsync(User, "waif", Now));
    }
}
