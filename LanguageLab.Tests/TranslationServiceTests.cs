using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class TranslationServiceTests
{
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;
    private static readonly LearnerLanguage Pl = LearnerLanguages.Find("pl")!;
    private const long User = 5;
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-29T12:00:00Z");

    private static TranslationService Service(
        ApplicationDbContext db, ITranslator translator, UncachedTranslationLimiter? limiter = null) =>
        new(db, translator, limiter ?? new UncachedTranslationLimiter(TimeProvider.System));

    private sealed class FakeTranslator : ITranslator
    {
        private readonly string? _answer;

        public int Calls { get; private set; }
        public LearnerLanguage? LastTarget { get; private set; }

        public FakeTranslator(string? answer) => _answer = answer;

        public Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken)
        {
            Calls++;
            LastTarget = target;
            return Task.FromResult(_answer);
        }
    }

    private static async Task<ApplicationDbContext> SeedAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.Add(new TelegramUser { Id = 5, TelegramUserId = 555 });
        db.Words.AddRange(
            TestWords.Pair(1, "apple", "яблуко"),
            TestWords.Pair(2, "orphan", null),
            TestWords.Pair(3, "run", "запускати", ownerId: 5));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return db;
    }

    [Fact]
    public async Task A_translated_shared_word_answers_without_the_provider()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("wrong");

        var result = await Service(db, provider).LookupAsync(User, "apple", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("apple", "яблуко", TranslationSource.Dictionary), result);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task An_untranslated_shared_word_asks_the_provider()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("сирота");

        var result = await Service(db, provider).LookupAsync(User, "orphan", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("orphan", "сирота", TranslationSource.Llm), result);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>Someone's personal translation is theirs, not a suggestion for everyone.</summary>
    [Fact]
    public async Task An_owned_word_is_not_a_shared_answer()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("бігти");

        var result = await Service(db, provider).LookupAsync(User, "run", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("run", "бігти", TranslationSource.Llm), result);
    }

    [Fact]
    public async Task No_answer_anywhere_is_none()
    {
        await using var db = await SeedAsync();

        var result = await Service(db, new FakeTranslator(null)).LookupAsync(User, "zzz", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("zzz", null, TranslationSource.None), result);
    }

    [Fact]
    public async Task A_provider_answer_becomes_a_shared_machine_row()
    {
        await using var db = await SeedAsync();

        await Service(db, new FakeTranslator("бездомна дитина")).LookupAsync(User, "waif", Uk, CancellationToken.None);

        var row = await db.Words.SingleAsync(w => w.Word == "waif");
        Assert.Null(row.OwnerId);
        var translation = db.WordTranslations.Single(t => t.WordPairId == row.Id && t.Language == "uk");
        Assert.Equal("бездомна дитина", translation.Text);
        Assert.Equal(TranslationOrigin.Machine, translation.Origin);
    }

    [Fact]
    public async Task A_provider_answer_fills_an_empty_shared_row()
    {
        await using var db = await SeedAsync();

        await Service(db, new FakeTranslator("сирота")).LookupAsync(User, "orphan", Uk, CancellationToken.None);

        var row = await db.Words.SingleAsync(w => w.Word == "orphan");
        Assert.Equal(2, row.Id);
        var translation = db.WordTranslations.Single(t => t.WordPairId == 2 && t.Language == "uk");
        Assert.Equal("сирота", translation.Text);
        Assert.Equal(TranslationOrigin.Machine, translation.Origin);
    }

    /// <summary>The whole point of caching: anyone's second lookup costs no provider call.</summary>
    [Fact]
    public async Task The_second_lookup_is_answered_from_the_dictionary()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("бездомна дитина");
        var service = Service(db, provider);

        await service.LookupAsync(User, "waif", Uk, CancellationToken.None);
        var second = await service.LookupAsync(User, "waif", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("waif", "бездомна дитина", TranslationSource.Dictionary), second);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task No_answer_creates_no_row()
    {
        await using var db = await SeedAsync();

        await Service(db, new FakeTranslator(null)).LookupAsync(User, "zzz", Uk, CancellationToken.None);

        Assert.False(await db.Words.AnyAsync(w => w.Word == "zzz"));
    }

    /// <summary>The user's own translation stays theirs; the shared row is a separate one.</summary>
    [Fact]
    public async Task A_lookup_never_touches_a_personal_row()
    {
        await using var db = await SeedAsync();

        await Service(db, new FakeTranslator("бігти")).LookupAsync(User, "run", Uk, CancellationToken.None);

        Assert.Equal("запускати", db.WordTranslations.Single(t => t.WordPairId == 3 && t.Language == "uk").Text);
        var sharedRun = await db.Words.SingleAsync(w => w.Word == "run" && w.OwnerId == null);
        Assert.Equal("бігти", db.WordTranslations.Single(t => t.WordPairId == sharedRun.Id && t.Language == "uk").Text);
    }

    /// <summary>Review focus 3.</summary>
    [Fact]
    public async Task A_uk_translation_does_not_answer_a_pl_lookup()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("jabłko");

        var result = await Service(db, provider).LookupAsync(User, "apple", Pl, CancellationToken.None);

        Assert.Equal(new TranslationLookup("apple", "jabłko", TranslationSource.Llm), result);
        Assert.Equal("pl", provider.LastTarget!.Code);
        Assert.Equal("jabłko", db.WordTranslations.Single(t => t.WordPairId == 1 && t.Language == "pl").Text);
        Assert.Equal("яблуко", db.WordTranslations.Single(t => t.WordPairId == 1 && t.Language == "uk").Text);
    }

    [Fact]
    public async Task A_cached_pl_translation_answers_the_next_pl_lookup_for_free()
    {
        await using var db = await SeedAsync();
        await Service(db, new FakeTranslator("jabłko")).LookupAsync(User, "apple", Pl, CancellationToken.None);
        var provider = new FakeTranslator("wrong");

        var result = await Service(db, provider).LookupAsync(User, "apple", Pl, CancellationToken.None);

        Assert.Equal(TranslationSource.Dictionary, result.Source);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task A_personal_translation_never_answers_a_shared_lookup()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("бігти");

        var result = await Service(db, provider).LookupAsync(User, "run", Uk, CancellationToken.None);

        Assert.Equal(TranslationSource.Llm, result.Source);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>
    /// Final whole-branch review, fix 3: a uk lookup tracks the shared WordPair with its uk
    /// WordTranslation attached. A later pl lookup of the same word, on the same DbContext and
    /// without a ChangeTracker.Clear() in between, re-queries with a Translations.Where(pl)
    /// Include — but the already-tracked uk row stays fixed up into the navigation regardless,
    /// so an unfiltered FirstOrDefault() on it would answer with the Ukrainian text under a
    /// false Dictionary source. This is the same class of bug already fixed in
    /// PersonalDictionaryService.UpdateTranslationAsync.
    /// </summary>
    [Fact]
    public async Task A_uk_lookup_does_not_leak_into_a_later_pl_lookup_on_the_same_context()
    {
        await using var db = await SeedAsync();
        var service = Service(db, new FakeTranslator("jabłko"));

        var ukResult = await service.LookupAsync(User, "apple", Uk, CancellationToken.None);
        Assert.Equal(TranslationSource.Dictionary, ukResult.Source);
        Assert.Equal("яблуко", ukResult.Translation);

        var plResult = await service.LookupAsync(User, "apple", Pl, CancellationToken.None);

        Assert.NotEqual(TranslationSource.Dictionary, plResult.Source);
        Assert.NotEqual("яблуко", plResult.Translation);
        Assert.Equal("jabłko", plResult.Translation);
    }

    [Fact]
    public async Task A_second_miss_inside_the_window_is_rate_limited_and_the_provider_is_not_asked()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator(null);
        var clock = new ManualTimeProvider(Start);
        var service = Service(db, provider, new UncachedTranslationLimiter(clock));

        await service.LookupAsync(User, "orphan", Uk, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(4));
        var second = await service.LookupAsync(User, "zzz", Uk, CancellationToken.None);

        Assert.Equal(new TranslationLookup("zzz", null, TranslationSource.RateLimited, 6), second);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task A_rate_limited_miss_writes_nothing()
    {
        await using var db = await SeedAsync();
        var service = Service(db, new FakeTranslator("x"), new UncachedTranslationLimiter(new ManualTimeProvider(Start)));

        await service.LookupAsync(User, "orphan", Uk, CancellationToken.None);
        await service.LookupAsync(User, "waif", Uk, CancellationToken.None);

        Assert.False(await db.Words.AnyAsync(w => w.Word == "waif"));
    }

    [Fact]
    public async Task Cache_hits_right_after_a_miss_are_never_rate_limited()
    {
        await using var db = await SeedAsync();
        var service = Service(db, new FakeTranslator("сирота"), new UncachedTranslationLimiter(new ManualTimeProvider(Start)));

        await service.LookupAsync(User, "orphan", Uk, CancellationToken.None);
        var hits = new List<TranslationLookup>();
        for (var i = 0; i < 3; i++)
        {
            hits.Add(await service.LookupAsync(User, "apple", Uk, CancellationToken.None));
        }

        Assert.All(hits, hit => Assert.Equal(new TranslationLookup("apple", "яблуко", TranslationSource.Dictionary), hit));
    }

    [Fact]
    public async Task Another_users_miss_does_not_spend_this_users_slot()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator(null);
        var service = Service(db, provider, new UncachedTranslationLimiter(new ManualTimeProvider(Start)));

        await service.LookupAsync(User, "orphan", Uk, CancellationToken.None);
        var other = await service.LookupAsync(User + 1, "zzz", Uk, CancellationToken.None);

        Assert.Equal(TranslationSource.None, other.Source);
        Assert.Equal(2, provider.Calls);
    }
}
