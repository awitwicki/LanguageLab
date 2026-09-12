using LanguageLab.Application.Translation;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class TranslationServiceTests
{
    private sealed class FakeTranslator : ITranslator
    {
        private readonly string? _answer;

        public int Calls { get; private set; }

        public FakeTranslator(string? answer) => _answer = answer;

        public Task<string?> TranslateAsync(string word, CancellationToken cancellationToken)
        {
            Calls++;
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
            new WordPair { Id = 1, Word = "apple", Translation = "яблуко" },
            new WordPair { Id = 2, Word = "orphan", Translation = "" },
            new WordPair { Id = 3, Word = "run", Translation = "запускати", OwnerId = 5 });
        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task A_translated_shared_word_answers_without_the_provider()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("wrong");

        var result = await new TranslationService(db, provider).LookupAsync("apple", CancellationToken.None);

        Assert.Equal(new TranslationLookup("apple", "яблуко", TranslationSource.Dictionary), result);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task An_untranslated_shared_word_asks_the_provider()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("сирота");

        var result = await new TranslationService(db, provider).LookupAsync("orphan", CancellationToken.None);

        Assert.Equal(new TranslationLookup("orphan", "сирота", TranslationSource.MyMemory), result);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>Someone's personal translation is theirs, not a suggestion for everyone.</summary>
    [Fact]
    public async Task An_owned_word_is_not_a_shared_answer()
    {
        await using var db = await SeedAsync();
        var provider = new FakeTranslator("бігти");

        var result = await new TranslationService(db, provider).LookupAsync("run", CancellationToken.None);

        Assert.Equal(new TranslationLookup("run", "бігти", TranslationSource.MyMemory), result);
    }

    [Fact]
    public async Task No_answer_anywhere_is_none()
    {
        await using var db = await SeedAsync();

        var result = await new TranslationService(db, new FakeTranslator(null)).LookupAsync("zzz", CancellationToken.None);

        Assert.Equal(new TranslationLookup("zzz", null, TranslationSource.None), result);
    }
}
