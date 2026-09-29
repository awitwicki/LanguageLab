using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class SortingEndpointsTests
{
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;

    private sealed class FakeTranslator : ITranslator
    {
        private readonly string? _answer;
        public int Calls { get; private set; }

        public FakeTranslator(string? answer) => _answer = answer;

        public Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_answer);
        }
    }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Marking_unknown_translates_an_untranslated_shared_word()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("сирота");

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, translator), SortStatus.Unknown, 1, Uk, CancellationToken.None);

        Assert.Equal(1, translator.Calls);
        Assert.Equal("сирота", db.WordTranslations.Single(t => t.WordPairId == 1 && t.Language == "uk").Text);
    }

    [Fact]
    public async Task Marking_unknown_on_an_already_translated_word_asks_nothing()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "apple", "яблуко"));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("wrong");

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, translator), SortStatus.Unknown, 1, Uk, CancellationToken.None);

        Assert.Equal(0, translator.Calls);
    }

    [Fact]
    public async Task Marking_unknown_on_a_personal_word_asks_nothing()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null, ownerId: 9));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("сирота");

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, translator), SortStatus.Unknown, 1, Uk, CancellationToken.None);

        Assert.Equal(0, translator.Calls);
    }

    [Theory]
    [InlineData(SortStatus.Known)]
    [InlineData(SortStatus.Excluded)]
    public async Task Marking_known_or_excluded_never_translates(SortStatus status)
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("сирота");

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, translator), status, 1, Uk, CancellationToken.None);

        Assert.Equal(0, translator.Calls);
    }

    /// <summary>Review focus: marking "don't know" twice must not double-translate.</summary>
    [Fact]
    public async Task Marking_unknown_twice_translates_only_once()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("сирота");
        var sorting = new WordSortingService(db);
        var translation = new TranslationService(db, translator);

        await SortingEndpoints.TranslateIfUnknownAsync(sorting, translation, SortStatus.Unknown, 1, Uk, CancellationToken.None);
        await SortingEndpoints.TranslateIfUnknownAsync(sorting, translation, SortStatus.Unknown, 1, Uk, CancellationToken.None);

        Assert.Equal(1, translator.Calls);
    }

    /// <summary>Review focus: a translator that answers nothing must not fail the caller.</summary>
    [Fact]
    public async Task A_translator_with_no_answer_does_not_throw()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null));
        await db.SaveChangesAsync();

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, new FakeTranslator(null)), SortStatus.Unknown, 1, Uk, CancellationToken.None);

        Assert.False(await db.WordTranslations.AnyAsync(t => t.WordPairId == 1));
    }

    /// <summary>
    /// Final review, finding 4: MarkAsync already saved the shelf change by the time this runs,
    /// so a missing language must be a graceful no-op here, not a throw the caller could turn
    /// into a 409 for an action that already succeeded.
    /// </summary>
    [Fact]
    public async Task No_language_set_translates_nothing()
    {
        await using var db = NewContext();
        db.Words.Add(TestWords.Pair(1, "orphan", null));
        await db.SaveChangesAsync();
        var translator = new FakeTranslator("сирота");

        await SortingEndpoints.TranslateIfUnknownAsync(
            new WordSortingService(db), new TranslationService(db, translator), SortStatus.Unknown, 1, null, CancellationToken.None);

        Assert.Equal(0, translator.Calls);
    }
}
