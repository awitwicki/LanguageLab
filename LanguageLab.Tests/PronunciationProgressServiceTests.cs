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
    public async Task An_empty_user_has_only_the_first_family_available()
    {
        await using var db = NewContext();
        var view = await new PronunciationProgressService(db).GetOverviewAsync(1);

        Assert.Equal(PronunciationCatalog.Families.Count, view.Families.Count);
        Assert.Equal(FamilyStatus.Available, view.Families[0].Status);
    }

    [Fact]
    public async Task GetFamilyAsync_returns_null_for_a_locked_family()
    {
        if (PronunciationCatalog.Families.Count < 2)
        {
            return;
        }

        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var lockedKey = PronunciationCatalog.Families[1].Key;

        var view = await service.GetFamilyAsync(1, lockedKey);

        Assert.Null(view);
    }

    [Fact]
    public async Task GetFamilyAsync_returns_null_for_an_unknown_family()
    {
        await using var db = NewContext();
        var view = await new PronunciationProgressService(db).GetFamilyAsync(1, "not-a-real-family-xyz");

        Assert.Null(view);
    }

    [Fact]
    public async Task NextWordAsync_returns_the_first_new_word_in_family_order()
    {
        await using var db = NewContext();
        var service = new PronunciationProgressService(db);
        var familyKey = PronunciationCatalog.Families[0].Key;
        var expected = PronunciationCatalog.WordsOf(familyKey)[0].Word;

        var (available, word) = await service.NextWordAsync(1, familyKey, includeMastered: false);

        Assert.True(available);
        Assert.Equal(expected, word!.Word);
    }

    [Fact]
    public async Task NextWordAsync_reports_unavailable_for_a_locked_family()
    {
        if (PronunciationCatalog.Families.Count < 2)
        {
            return;
        }

        await using var db = NewContext();
        var service = new PronunciationProgressService(db);

        var (available, word) = await service.NextWordAsync(1, PronunciationCatalog.Families[1].Key, includeMastered: false);

        Assert.False(available);
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
}
