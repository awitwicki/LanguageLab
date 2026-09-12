using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class SchemaTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// Join-таблиця стала сутністю з навантаженням, але скіп-навігація має вціліти:
    /// на неї спирається LearnableQuery і кнопки бота.
    /// </summary>
    [Fact]
    public async Task Skip_navigation_survives_payload_join()
    {
        await using var db = NewContext();

        var dictionary = new Domain.Entities.Dictionary { Id = 1, Name = "book", WordsCount = 1 };
        var word = new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" };
        dictionary.Words = [word];

        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        var found = await db.Words.CountAsync(w => w.Dictionaries.Any(d => d.Id == 1));

        Assert.Equal(1, found);
    }

    /// <summary>Частота живе на join-рядку, а не на слові: одне слово має різну частоту в різних книжках.</summary>
    [Fact]
    public async Task Frequency_is_stored_per_dictionary()
    {
        await using var db = NewContext();

        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 47 });
        db.DictionaryWords.Add(new DictionaryWord { DictionaryId = 2, WordPairId = 1, Frequency = 3 });
        await db.SaveChangesAsync();

        var frequencies = await db.DictionaryWords
            .Where(dw => dw.WordPairId == 1)
            .OrderBy(dw => dw.DictionaryId)
            .Select(dw => dw.Frequency)
            .ToListAsync();

        Assert.Equal([47, 3], frequencies);
    }

    /// <summary>Глави прив'язані до словника й нумеруються з нуля.</summary>
    [Fact]
    public async Task Chapters_belong_to_dictionary()
    {
        await using var db = NewContext();

        var dictionary = new Domain.Entities.Dictionary { Id = 1, Name = "book", WordsCount = 0 };
        dictionary.Chapters =
        [
            new Chapter { Id = 1, Order = 0, Title = "Chapter 1", WordsCount = 10 },
            new Chapter { Id = 2, Order = 1, Title = "Chapter 2", WordsCount = 12 }
        ];

        db.Dictionaries.Add(dictionary);
        await db.SaveChangesAsync();

        var titles = await db.Chapters
            .Where(c => c.DictionaryId == 1)
            .OrderBy(c => c.Order)
            .Select(c => c.Title)
            .ToListAsync();

        Assert.Equal(["Chapter 1", "Chapter 2"], titles);
    }

    /// <summary>Deleting a user takes their shelves and progress with them: no orphan rows keyed to a gone user.</summary>
    [Fact]
    public async Task Deleting_a_user_removes_their_shelves_and_progress()
    {
        await using var db = NewContext();

        var user = new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = DateTime.UtcNow };
        var word = new WordPair { Id = 1, Word = "abide", Translation = "дотримуватися" };

        db.Users.Add(user);
        db.Words.Add(word);
        db.KnownWords.Add(new KnownWord { Id = 1, UserId = 1, WordPairId = 1, CreatedAt = DateTime.UtcNow });
        db.WordProgresses.Add(new WordProgress { Id = 1, UserId = 1, WordPairId = 1, Box = 1 });
        await db.SaveChangesAsync();

        // The in-memory provider cascades through the change tracker, so the dependents
        // must be loaded for the cascade to be observable — the relational provider
        // does the same work in the database via ON DELETE CASCADE.
        await db.KnownWords.ToListAsync();
        await db.WordProgresses.ToListAsync();

        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == 1));
        await db.SaveChangesAsync();

        Assert.Empty(await db.KnownWords.ToListAsync());
        Assert.Empty(await db.WordProgresses.ToListAsync());
        Assert.Single(await db.Words.ToListAsync());
    }

    /// <summary>A deleted user must not take their dictionaries with them: the book survives, ownerless.</summary>
    [Fact]
    public async Task Deleting_a_user_keeps_their_dictionaries_and_clears_the_owner()
    {
        await using var db = NewContext();

        db.Users.Add(new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = DateTime.UtcNow });
        db.Dictionaries.Add(new Domain.Entities.Dictionary
        {
            Id = 1, Name = "Wool", WordsCount = 0, OwnerId = 1, IsPublic = true,
        });
        await db.SaveChangesAsync();

        await db.Dictionaries.ToListAsync();

        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == 1));
        await db.SaveChangesAsync();

        var dictionary = await db.Dictionaries.FirstAsync(d => d.Id == 1);

        Assert.Null(dictionary.OwnerId);
        Assert.True(dictionary.IsPublic);
    }

    /// <summary>The trainer's rows hang off the user like shelves do: deleting the account deletes them all.</summary>
    [Fact]
    public async Task Deleting_a_user_removes_their_verb_progress_sessions_tasks_and_attempts()
    {
        await using var db = NewContext();

        var now = DateTime.UtcNow;
        db.Users.Add(new TelegramUser { Id = 1, TelegramUserId = 777, CreatedAt = now });
        db.VerbProgresses.Add(new VerbProgress { Id = 1, UserId = 1, Verb = "go", State = VerbState.Learning1, LastSeenAt = now });
        db.VerbSessions.Add(new VerbSession { Id = 1, UserId = 1, Mode = SessionMode.Learn, Group = 4, Family = "core", StartedAt = now });
        db.VerbTasks.Add(new VerbTask { Id = 1, SessionId = 1, Order = 0, Type = ExerciseType.Card, Verb = "go", FormAsked = FormAsked.Recognition, Level = 1 });
        db.VerbAttempts.Add(new VerbAttempt
        {
            Id = 1, UserId = 1, Verb = "go", SessionId = 1, TaskId = 1, Type = ExerciseType.Card,
            FormAsked = FormAsked.Recognition, AnswerGiven = "seen", Outcome = AttemptOutcome.Correct, CreatedAt = now,
        });
        await db.SaveChangesAsync();

        await db.VerbProgresses.ToListAsync();
        await db.VerbSessions.ToListAsync();
        await db.VerbTasks.ToListAsync();
        await db.VerbAttempts.ToListAsync();

        db.Users.Remove(await db.Users.FirstAsync(u => u.Id == 1));
        await db.SaveChangesAsync();

        Assert.Empty(await db.VerbProgresses.ToListAsync());
        Assert.Empty(await db.VerbSessions.ToListAsync());
        Assert.Empty(await db.VerbTasks.ToListAsync());
        Assert.Empty(await db.VerbAttempts.ToListAsync());
    }

    /// <summary>The trainer upserts a verb's standing by user + verb, so that key must be unique.</summary>
    [Fact]
    public void A_user_has_one_standing_per_verb()
    {
        using var db = NewContext();

        var index = db.Model
            .FindEntityType(typeof(VerbProgress))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(["UserId", "Verb"]));

        Assert.True(index.IsUnique);
    }

    /// <summary>A task's payload is JSON on the row; the typed view must survive the round trip.</summary>
    [Fact]
    public void Task_payload_round_trips_through_the_column()
    {
        var task = new VerbTask { SessionId = 1, Verb = "go", Type = ExerciseType.GapChoice };
        task.SetPayload(new TaskPayload { Sentence = "They ___ home.", Options = ["went", "goed"], Correct = "went" });

        var payload = task.GetPayload();

        Assert.Equal("They ___ home.", payload.Sentence);
        Assert.Equal(["went", "goed"], payload.Options);
        Assert.Equal("went", payload.Correct);
    }

    /// <summary>Login upserts by TelegramUserId, so the column must not allow a second row with the same id.</summary>
    [Fact]
    public void Telegram_user_id_is_unique()
    {
        using var db = NewContext();

        var index = db.Model
            .FindEntityType(typeof(TelegramUser))!
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(TelegramUser.TelegramUserId)));

        Assert.True(index.IsUnique);
    }
}
