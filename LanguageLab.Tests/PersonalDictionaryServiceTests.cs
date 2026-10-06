using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class PersonalDictionaryServiceTests
{
    private const long UserId = 1;
    private const long OtherId = 2;
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<ApplicationDbContext> NewContextAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = UserId, TelegramUserId = 11 },
            new TelegramUser { Id = OtherId, TelegramUserId = 22 });
        await db.SaveChangesAsync();

        return db;
    }

    private static PersonalDictionaryService Service(ApplicationDbContext db) =>
        new(db, new WordSelectionService(db), new LearningProgressService(db));

    [Fact]
    public async Task GetOrCreate_creates_the_dictionary_once()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var first = await service.GetOrCreateAsync(UserId);
        var second = await service.GetOrCreateAsync(UserId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Dictionaries.CountAsync());
        Assert.Equal(PersonalDictionaryService.Name, first.Name);
        Assert.True(first.IsPersonal);
        Assert.Equal(PublicationStatus.Private, first.PublicationStatus);
        Assert.Equal(UserId, first.OwnerId);
    }

    [Fact]
    public async Task Add_normalizes_shelves_and_counts_the_word()
    {
        await using var db = await NewContextAsync();

        var added = await Service(db).AddAsync(UserId, TestWords.Uk, "  Apple ", " яблуко ", Now);

        Assert.NotNull(added);
        Assert.Equal("apple", added.Word);
        Assert.Equal("яблуко", added.Translation);
        Assert.Null(added.Box);
        Assert.False(added.IsLearned);

        var pair = await db.Words.SingleAsync();
        Assert.Equal(UserId, pair.OwnerId);

        var dictionary = await db.Dictionaries.SingleAsync(d => d.IsPersonal && d.OwnerId == UserId);
        Assert.Equal(1, dictionary.WordsCount);
        Assert.True(await db.DictionaryWords.AnyAsync(dw => dw.DictionaryId == dictionary.Id && dw.WordPairId == pair.Id));
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == UserId && u.WordPairId == pair.Id));

        // Shelved "don't know" with a translation and no progress: exactly what a new batch picks up.
        Assert.Equal(1, await new WordSelectionService(db).CountLearnableAsync(UserId, TestWords.Uk, dictionary.Id));
    }

    [Fact]
    public async Task Add_twice_is_a_conflict()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now);
        var again = await service.AddAsync(UserId, TestWords.Uk, "APPLE", "інше", Now);

        Assert.Null(again);
        Assert.Equal(1, await db.Words.CountAsync());
        Assert.Equal(1, (await db.Dictionaries.SingleAsync()).WordsCount);
    }

    [Theory]
    [InlineData("apple", "   ")]
    [InlineData("", "яблуко")]
    [InlineData("a1", "яблуко")]
    public async Task Add_rejects_invalid_input(string word, string translation)
    {
        await using var db = await NewContextAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).AddAsync(UserId, TestWords.Uk, word, translation, Now));
        Assert.Equal(0, await db.Words.CountAsync());
    }

    /// <summary>Owned rows are unique per user, not globally: two people may both add "apple".</summary>
    [Fact]
    public async Task Two_users_may_add_the_same_word()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var mine = await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now);
        var theirs = await service.AddAsync(OtherId, TestWords.Uk, "apple", "яблучко", Now);

        Assert.NotNull(mine);
        Assert.NotNull(theirs);
        Assert.NotEqual(mine.WordPairId, theirs.WordPairId);
        Assert.Equal(2, await db.Dictionaries.CountAsync(d => d.IsPersonal));
    }

    [Fact]
    public async Task Remove_tears_down_shelves_progress_and_questions()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var added = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;
        var dictionary = await db.Dictionaries.SingleAsync();

        db.WordProgresses.Add(new WordProgress
        {
            Id = 1, UserId = UserId, WordPairId = added.WordPairId, Box = 2, DueAt = Now.AddDays(3), LastSeenAt = Now,
        });
        db.Trainings.Add(new Training { Id = 1, UserId = UserId, DictionaryId = dictionary.Id, Mode = TrainingMode.NewBatch, CreatedAt = Now });
        db.TrainingQuestions.Add(new TrainingQuestion
        {
            Id = 1, UserId = UserId, TrainingId = 1, WordPairId = added.WordPairId, Order = 0, CreatedAt = Now,
            Direction = QuestionDirection.EnToNative, OptionIds = [added.WordPairId],
        });
        await db.SaveChangesAsync();

        Assert.True(await service.RemoveAsync(UserId, added.WordPairId));

        Assert.Equal(0, await db.Words.CountAsync());
        Assert.Equal(0, await db.UnknownWords.CountAsync());
        Assert.Equal(0, await db.WordProgresses.CountAsync());
        Assert.Equal(0, await db.TrainingQuestions.CountAsync());
        Assert.Equal(0, await db.DictionaryWords.CountAsync());
        Assert.Equal(0, (await db.Dictionaries.SingleAsync()).WordsCount);
    }

    [Fact]
    public async Task Remove_refuses_another_users_word()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var theirs = (await service.AddAsync(OtherId, TestWords.Uk, "apple", "яблуко", Now))!;

        Assert.False(await service.RemoveAsync(UserId, theirs.WordPairId));
        Assert.Equal(1, await db.Words.CountAsync());
    }

    [Fact]
    public async Task Remove_of_an_unknown_id_is_false()
    {
        await using var db = await NewContextAsync();

        Assert.False(await Service(db).RemoveAsync(UserId, 999));
    }

    /// <summary>
    /// A correction of the meaning, not a new word: the shelf and the Leitner progress the word
    /// already earned stay exactly where they were.
    /// </summary>
    [Fact]
    public async Task An_overlong_translation_is_refused_on_add()
    {
        await using var db = await NewContextAsync();
        var tooLong = new string('я', WordTranslation.MaxTextLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).AddAsync(UserId, TestWords.Uk, "apple", tooLong, Now));
    }

    [Fact]
    public async Task An_overlong_translation_is_refused_on_update()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var added = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;
        var tooLong = new string('я', WordTranslation.MaxTextLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateTranslationAsync(UserId, TestWords.Uk, added.WordPairId, tooLong));
    }

    [Fact]
    public async Task UpdateTranslation_rewrites_the_translation_and_keeps_the_shelf_and_progress()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var added = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;

        db.WordProgresses.Add(new WordProgress
        {
            Id = 1, UserId = UserId, WordPairId = added.WordPairId, Box = 2, DueAt = Now.AddDays(3), LastSeenAt = Now,
        });
        await db.SaveChangesAsync();

        var updated = await service.UpdateTranslationAsync(UserId, TestWords.Uk, added.WordPairId, "  яблуко, яблуня  ");

        Assert.NotNull(updated);
        Assert.Equal(added.WordPairId, updated.WordPairId);
        Assert.Equal("apple", updated.Word);
        Assert.Equal("яблуко, яблуня", updated.Translation);
        Assert.Equal(2, updated.Box);
        Assert.False(updated.IsLearned);

        Assert.Equal("яблуко, яблуня", (await db.WordTranslations.SingleAsync(t => t.WordPairId == added.WordPairId)).Text);
        Assert.True(await db.UnknownWords.AnyAsync(u => u.UserId == UserId && u.WordPairId == added.WordPairId));
        Assert.Equal(2, (await db.WordProgresses.SingleAsync()).Box);
    }

    /// <summary>A translation the user typed is Manual, whatever a provider had left on the row.</summary>
    [Fact]
    public async Task UpdateTranslation_marks_the_translation_manual()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var added = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;

        var translation = await db.WordTranslations.SingleAsync(t => t.WordPairId == added.WordPairId);
        translation.Origin = TranslationOrigin.Machine;
        await db.SaveChangesAsync();

        await service.UpdateTranslationAsync(UserId, TestWords.Uk, added.WordPairId, "яблуня");

        Assert.Equal(TranslationOrigin.Manual, (await db.WordTranslations.SingleAsync(t => t.WordPairId == added.WordPairId)).Origin);
    }

    [Fact]
    public async Task UpdateTranslation_refuses_an_empty_translation()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var added = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateTranslationAsync(UserId, TestWords.Uk, added.WordPairId, "   "));

        Assert.Equal("яблуко", (await db.WordTranslations.SingleAsync(t => t.WordPairId == added.WordPairId)).Text);
    }

    [Fact]
    public async Task UpdateTranslation_refuses_another_users_word()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var theirs = (await service.AddAsync(OtherId, TestWords.Uk, "apple", "яблуко", Now))!;

        Assert.Null(await service.UpdateTranslationAsync(UserId, TestWords.Uk, theirs.WordPairId, "не моє"));
        Assert.Equal("яблуко", (await db.WordTranslations.SingleAsync(t => t.WordPairId == theirs.WordPairId)).Text);
    }

    [Fact]
    public async Task UpdateTranslation_of_an_unknown_id_is_null()
    {
        await using var db = await NewContextAsync();

        Assert.Null(await Service(db).UpdateTranslationAsync(UserId, TestWords.Uk, 999, "яблуко"));
    }

    [Fact]
    public async Task Get_lists_newest_first_with_learning_state()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        var apple = (await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now))!;
        var run = (await service.AddAsync(UserId, TestWords.Uk, "run", "бігти", Now))!;
        var learned = (await service.AddAsync(UserId, TestWords.Uk, "done", "готово", Now))!;

        db.WordProgresses.AddRange(
            new WordProgress { Id = 1, UserId = UserId, WordPairId = apple.WordPairId, Box = 2, DueAt = Now.AddDays(-1), LastSeenAt = Now },
            new WordProgress { Id = 2, UserId = UserId, WordPairId = learned.WordPairId, Box = 5, DueAt = null, IsLearned = true, LastSeenAt = Now });
        await db.SaveChangesAsync();

        var view = await service.GetAsync(UserId, TestWords.Uk, Now);

        Assert.Equal(PersonalDictionaryService.Name, view.Name);
        Assert.Equal(3, view.WordsCount);
        Assert.Equal(new[] { "done", "run", "apple" }, view.Words.Select(w => w.Word));
        Assert.Equal(new int?[] { 5, null, 2 }, view.Words.Select(w => w.Box));
        Assert.Equal(new[] { true, false, false }, view.Words.Select(w => w.IsLearned));
        Assert.Equal(1, view.LearnableCount);
        Assert.Equal(1, view.DueCount);
        Assert.Equal(3, view.Learning.Total);
        Assert.Equal(1, view.Learning.NotStarted);
        Assert.Equal(1, view.Learning.Learned);
        Assert.Equal(run.WordPairId, view.Words[1].WordPairId);
    }

    [Fact]
    public async Task Get_creates_the_dictionary_for_a_new_user()
    {
        await using var db = await NewContextAsync();

        var view = await Service(db).GetAsync(UserId, TestWords.Uk, Now);

        Assert.Empty(view.Words);
        Assert.Equal(0, view.WordsCount);
        Assert.Equal(1, await db.Dictionaries.CountAsync(d => d.IsPersonal && d.OwnerId == UserId));
    }

    [Fact]
    public async Task A_word_is_saved_in_the_learners_language()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var added = await service.AddAsync(UserId, "pl", "apple", "jabłko", Now);

        Assert.Equal("jabłko", added!.Translation);
        var row = db.WordTranslations.Single(t => t.WordPairId == added.WordPairId);
        Assert.Equal(("pl", "jabłko", TranslationOrigin.Manual), (row.Language, row.Text, row.Origin));
    }

    /// <summary>Review focus 5.</summary>
    [Fact]
    public async Task Editing_after_a_switch_adds_the_new_language_and_keeps_the_old()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var added = await service.AddAsync(UserId, "uk", "apple", "яблуко", Now);

        var listed = (await service.GetAsync(UserId, "pl", Now)).Words.Single();
        Assert.Equal("", listed.Translation);
        Assert.Equal(0, (await service.GetAsync(UserId, "pl", Now)).LearnableCount);

        var edited = await service.UpdateTranslationAsync(UserId, "pl", added!.WordPairId, "jabłko");

        Assert.Equal("jabłko", edited!.Translation);
        Assert.Equal("яблуко", db.WordTranslations.Single(t => t.WordPairId == added.WordPairId && t.Language == "uk").Text);
        Assert.Equal(1, (await service.GetAsync(UserId, "pl", Now)).LearnableCount);
    }

    [Fact]
    public async Task AddMany_adds_new_words_and_reports_duplicates()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);
        await service.AddAsync(UserId, TestWords.Uk, "apple", "яблуко", Now);

        var outcomes = await service.AddManyAsync(UserId, TestWords.Uk,
        [
            new BulkWordEntry("apple", "інше"),
            new BulkWordEntry("Banana", " банан "),
            new BulkWordEntry("banana", "ще раз"),
        ], Now);

        Assert.Equal(3, outcomes.Count);

        Assert.False(outcomes[0].Added);
        Assert.Equal("Already in your dictionary.", outcomes[0].Error);

        Assert.True(outcomes[1].Added);
        Assert.Equal("banana", outcomes[1].Word);
        Assert.Equal("банан", outcomes[1].Translation);
        Assert.Null(outcomes[1].Error);

        // The second "banana" line is a duplicate of the first, not of the pre-existing "apple".
        Assert.False(outcomes[2].Added);
        Assert.Equal("Already in your dictionary.", outcomes[2].Error);

        Assert.Equal(2, await db.Words.CountAsync());
        Assert.Equal(2, (await db.Dictionaries.SingleAsync()).WordsCount);
    }

    [Fact]
    public async Task AddMany_reports_invalid_lines_without_stopping_the_batch()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var outcomes = await service.AddManyAsync(UserId, TestWords.Uk,
        [
            new BulkWordEntry("bad!", "щось"),
            new BulkWordEntry("kiwi", "   "),
            new BulkWordEntry("mango", "манго"),
        ], Now);

        Assert.False(outcomes[0].Added);
        Assert.Contains("letters", outcomes[0].Error);

        Assert.False(outcomes[1].Added);
        Assert.Equal("The translation cannot be empty.", outcomes[1].Error);

        Assert.True(outcomes[2].Added);
        Assert.Null(outcomes[2].Error);

        Assert.Equal(1, await db.Words.CountAsync());
    }

    [Fact]
    public async Task A_bulk_import_larger_than_the_cap_is_refused_before_any_row_is_written()
    {
        await using var db = await NewContextAsync();
        var service = Service(db);

        var entries = Enumerable.Range(0, PersonalDictionaryService.MaxBulkEntries + 1)
            .Select(i => new BulkWordEntry($"word{i}", "переклад"))
            .ToList();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddManyAsync(userId: 1, TestWords.Uk, entries, DateTime.UtcNow));

        Assert.Contains("at a time", error.Message);
        Assert.False(await db.Words.AnyAsync());
    }
}
