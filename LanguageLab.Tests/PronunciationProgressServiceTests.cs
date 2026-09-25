using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Pronunciation;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class PronunciationProgressServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task An_empty_user_has_every_family_available()
    {
        await using var db = NewContext();
        var view = await new PronunciationProgressService(db).GetOverviewAsync(1);

        Assert.Equal(PronunciationCatalog.Families.Count, view.Families.Count);
        Assert.All(view.Families, f => Assert.Equal(FamilyStatus.Available, f.Status));
    }

    [Fact]
    public async Task GetFamilyAsync_opens_the_last_family_to_a_user_with_no_progress()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var lastKey = PronunciationCatalog.Families[^1].Key;

        var view = await service.GetFamilyAsync(1, lastKey);

        Assert.NotNull(view);
        Assert.Equal(lastKey, view.Key);
        Assert.All(view.Words, w => Assert.Equal(PronunciationState.New, w.State));
    }

    [Fact]
    public async Task GetFamilyAsync_returns_null_for_an_unknown_family()
    {
        await using var db = NewContext();
        var view = await new PronunciationProgressService(db).GetFamilyAsync(1, "not-a-real-family-xyz");

        Assert.Null(view);
    }

    [Fact]
    public async Task NextWordAsync_returns_the_first_new_word_in_family_order_for_any_family()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var familyKey = PronunciationCatalog.Families[^1].Key;
        var expected = PronunciationCatalog.WordsOf(familyKey)[0].Word;

        var (exists, word) = await service.NextWordAsync(1, familyKey, includeMastered: false);

        Assert.True(exists);
        Assert.Equal(expected, word!.Word);
    }

    [Fact]
    public async Task NextWordAsync_reports_an_unknown_family()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);

        var (exists, word) = await service.NextWordAsync(1, "not-a-real-family-xyz", includeMastered: false);

        Assert.False(exists);
        Assert.Null(word);
    }

    [Fact]
    public async Task RecordAttemptAsync_returns_null_for_a_word_not_in_the_catalog()
    {
        await using var db = NewContext();
        var result = await new PronunciationProgressService(db)
            .RecordAttemptAsync(1, "not-a-real-catalog-word-xyz", Accent.Us, "hello", Now);

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordAttemptAsync_grades_and_creates_progress_on_first_attempt()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var word = PronunciationCatalog.Words[0].Word;

        var result = await service.RecordAttemptAsync(1, word, Accent.Us, word, Now);

        Assert.NotNull(result);
        Assert.Equal(PronunciationOutcome.Correct, result!.Outcome);
        Assert.Equal(PronunciationState.Learning, result.State);
        Assert.Equal(1, result.Streak);
        Assert.Single(await db.PronunciationProgresses.ToListAsync());
        Assert.Single(await db.PronunciationAttempts.ToListAsync());
    }

    [Fact]
    public async Task RecordAttemptAsync_masters_a_word_after_three_correct_attempts()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var word = PronunciationCatalog.Words[0].Word;

        await service.RecordAttemptAsync(1, word, Accent.Us, word, Now);
        await service.RecordAttemptAsync(1, word, Accent.Us, word, Now.AddMinutes(1));
        var result = await service.RecordAttemptAsync(1, word, Accent.Us, word, Now.AddMinutes(2));

        Assert.Equal(PronunciationState.Mastered, result!.State);
    }

    [Fact]
    public async Task NextWordAsync_skips_mastered_words_unless_includeMastered_is_set()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var family = PronunciationCatalog.Families.FirstOrDefault(f => PronunciationCatalog.WordsOf(f.Key).Count >= 2);

        if (family is null)
        {
            return;
        }

        var familyKey = family.Key;
        var words = PronunciationCatalog.WordsOf(familyKey);

        // Master every word in the family.
        foreach (var w in words)
        {
            await service.RecordAttemptAsync(1, w.Word, Accent.Us, w.Word, Now);
            await service.RecordAttemptAsync(1, w.Word, Accent.Us, w.Word, Now.AddMinutes(1));
            await service.RecordAttemptAsync(1, w.Word, Accent.Us, w.Word, Now.AddMinutes(2));
        }

        var (_, withoutMastered) = await service.NextWordAsync(1, familyKey, includeMastered: false);
        var (_, withMastered) = await service.NextWordAsync(1, familyKey, includeMastered: true);

        Assert.Null(withoutMastered);
        Assert.NotNull(withMastered);
    }

    [Fact]
    public async Task ResetWordAsync_puts_a_practised_word_back_to_New()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var word = PronunciationCatalog.Words[0].Word;
        await service.RecordAttemptAsync(1, word, Accent.Us, word, Now);

        Assert.True(await service.ResetWordAsync(1, word));

        var view = await service.GetFamilyAsync(1, PronunciationCatalog.Find(word)!.FamilyKey);
        var after = view!.Words.Single(w => w.Word == word);
        Assert.Equal(PronunciationState.New, after.State);
        Assert.Equal(0, after.Streak);
    }

    [Fact]
    public async Task ResetWordAsync_keeps_the_attempts_it_already_logged()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var word = PronunciationCatalog.Words[0].Word;
        await service.RecordAttemptAsync(1, word, Accent.Us, word, Now);

        await service.ResetWordAsync(1, word);

        Assert.Single(await db.PronunciationAttempts.Where(a => a.UserId == 1 && a.Word == word).ToListAsync());
    }

    [Fact]
    public async Task ResetWordAsync_leaves_the_same_word_alone_for_another_user()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var word = PronunciationCatalog.Words[0].Word;
        await service.RecordAttemptAsync(1, word, Accent.Us, word, Now);
        await service.RecordAttemptAsync(2, word, Accent.Us, word, Now);

        await service.ResetWordAsync(1, word);

        var other = await db.PronunciationProgresses.SingleAsync(p => p.UserId == 2 && p.Word == word);
        Assert.Equal(PronunciationState.Learning, other.State);
    }

    [Fact]
    public async Task ResetWordAsync_is_a_no_op_for_a_word_never_practised()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);

        Assert.True(await service.ResetWordAsync(1, PronunciationCatalog.Words[0].Word));
    }

    [Fact]
    public async Task ResetWordAsync_reports_a_word_the_catalog_does_not_have()
    {
        await using var db = NewContext();

        Assert.False(await new PronunciationProgressService(db).ResetWordAsync(1, "notarealword"));
    }
}
