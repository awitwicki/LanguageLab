using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class RecentActivityServiceTests
{
    private const long Owner = 1;
    private const long Stranger = 2;
    private const long Wool = 10;
    private const long Hidden = 20;
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// "Wool" — the owner's published book, chapters 1 and 2, four words, none sorted.
    /// "Hidden" — a stranger's private book with chapter 3: visible to nobody but them.
    /// </summary>
    private static async Task<ApplicationDbContext> ArrangeAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.Users.AddRange(
            new TelegramUser { Id = Owner, TelegramUserId = 11 },
            new TelegramUser { Id = Stranger, TelegramUserId = 22 });

        db.Dictionaries.AddRange(
            new Domain.Entities.Dictionary
            {
                Id = Wool, Name = "Wool", OwnerId = Owner, WordsCount = 4,
                PublicationStatus = PublicationStatus.Published
            },
            new Domain.Entities.Dictionary
            {
                Id = Hidden, Name = "Hidden", OwnerId = Stranger, WordsCount = 1,
                PublicationStatus = PublicationStatus.Private
            });

        db.Chapters.AddRange(
            new Chapter { Id = 1, DictionaryId = Wool, Order = 0, Title = "Holston", WordsCount = 2 },
            new Chapter { Id = 2, DictionaryId = Wool, Order = 1, Title = "", WordsCount = 2 },
            new Chapter { Id = 3, DictionaryId = Hidden, Order = 0, Title = "Secret", WordsCount = 1 });

        var words = Enumerable.Range(1, 5)
            .Select(i => new WordPair { Id = i, Word = $"word{i}", Translation = $"переклад{i}" })
            .ToList();
        db.Words.AddRange(words);

        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = Wool, WordPairId = 1, Frequency = 4 },
            new DictionaryWord { DictionaryId = Wool, WordPairId = 2, Frequency = 3 },
            new DictionaryWord { DictionaryId = Wool, WordPairId = 3, Frequency = 2 },
            new DictionaryWord { DictionaryId = Wool, WordPairId = 4, Frequency = 1 },
            new DictionaryWord { DictionaryId = Hidden, WordPairId = 5, Frequency = 1 });

        db.ChapterWords.AddRange(
            new ChapterWord { ChapterId = 1, WordPairId = 1, Count = 4 },
            new ChapterWord { ChapterId = 1, WordPairId = 2, Count = 3 },
            new ChapterWord { ChapterId = 2, WordPairId = 3, Count = 2 },
            new ChapterWord { ChapterId = 2, WordPairId = 4, Count = 1 },
            new ChapterWord { ChapterId = 3, WordPairId = 5, Count = 1 });

        await db.SaveChangesAsync();
        return db;
    }

    private static RecentActivityService Service(ApplicationDbContext db) =>
        new(db, new DictionaryAccessService(db), new WordSortingService(db));

    /// <summary>A finished session of <paramref name="questions"/> questions, all answered correctly bar <paramref name="wrong"/>.</summary>
    private static async Task<Training> FinishedAsync(
        ApplicationDbContext db,
        long userId,
        long? dictionaryId,
        long? chapterId,
        TrainingMode mode,
        DateTime finishedAt,
        int questions = 2,
        int wrong = 0)
    {
        var training = new Training
        {
            UserId = userId,
            DictionaryId = dictionaryId,
            ChapterId = chapterId,
            Mode = mode,
            CreatedAt = finishedAt.AddMinutes(-5),
            FinishedAt = finishedAt
        };

        db.Trainings.Add(training);
        await db.SaveChangesAsync();

        for (var i = 0; i < questions; i++)
        {
            db.TrainingQuestions.Add(new TrainingQuestion
            {
                TrainingId = training.Id,
                UserId = userId,
                WordPairId = 1,
                CreatedAt = training.CreatedAt,
                Order = i,
                IsCorrect = i >= wrong,
                AnsweredAt = finishedAt
            });
        }

        await db.SaveChangesAsync();
        return training;
    }

    private static void Visit(ApplicationDbContext db, long dictionaryId, long? chapterId, DateTime at)
    {
        db.SortingVisits.Add(new SortingVisit
        {
            UserId = Owner, DictionaryId = dictionaryId, ChapterId = chapterId, LastSortedAt = at
        });
    }

    [Fact]
    public async Task Nothing_done_yet_is_two_empty_lists()
    {
        await using var db = await ArrangeAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Empty(recent.Exercises);
        Assert.Empty(recent.Sorting);
    }

    [Fact]
    public async Task An_exercise_carries_its_scope_and_score()
    {
        await using var db = await ArrangeAsync();
        var training = await FinishedAsync(
            db, Owner, Wool, chapterId: 1, TrainingMode.NewBatch, Now.AddHours(-1), questions: 4, wrong: 1);

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var exercise = Assert.Single(recent.Exercises);
        Assert.Equal(training.Id, exercise.TrainingId);
        Assert.Equal(TrainingMode.NewBatch, exercise.Mode);
        Assert.Equal(Wool, exercise.DictionaryId);
        Assert.Equal("Wool", exercise.DictionaryName);
        Assert.Equal(1, exercise.Chapter?.Id);
        Assert.Equal("Holston", exercise.Chapter?.Title);
        Assert.Equal(3, exercise.Correct);
        Assert.Equal(4, exercise.Total);
    }

    /// <summary>The review from the "Due today" tile spans every book, so it has no scope to name.</summary>
    [Fact]
    public async Task A_review_across_every_book_has_no_dictionary()
    {
        await using var db = await ArrangeAsync();
        await FinishedAsync(db, Owner, dictionaryId: null, chapterId: null, TrainingMode.Review, Now.AddHours(-1));

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var exercise = Assert.Single(recent.Exercises);
        Assert.Null(exercise.DictionaryId);
        Assert.Null(exercise.DictionaryName);
        Assert.Null(exercise.Chapter);
        Assert.Equal(TrainingMode.Review, exercise.Mode);
    }

    [Fact]
    public async Task Exercises_come_newest_first()
    {
        await using var db = await ArrangeAsync();
        await FinishedAsync(db, Owner, Wool, chapterId: 1, TrainingMode.NewBatch, Now.AddHours(-3));
        await FinishedAsync(db, Owner, Wool, chapterId: 2, TrainingMode.NewBatch, Now.AddHours(-1));
        await FinishedAsync(db, Owner, Wool, chapterId: null, TrainingMode.NewBatch, Now.AddHours(-2));

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Equal([2L, null, 1L], recent.Exercises.Select(e => e.Chapter?.Id));
    }

    /// <summary>
    /// Five batches of one chapter in an evening are one row, not five: the list is a way
    /// back into a scope, and the same scope twice is the same way back.
    /// </summary>
    [Fact]
    public async Task One_row_per_scope_and_mode()
    {
        await using var db = await ArrangeAsync();
        await FinishedAsync(db, Owner, Wool, chapterId: 1, TrainingMode.NewBatch, Now.AddHours(-3));
        await FinishedAsync(db, Owner, Wool, chapterId: 1, TrainingMode.NewBatch, Now.AddHours(-2));
        var newest = await FinishedAsync(db, Owner, Wool, chapterId: 1, TrainingMode.Review, Now.AddHours(-1));

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Equal(2, recent.Exercises.Count);
        Assert.Equal(newest.Id, recent.Exercises[0].TrainingId);
        Assert.Equal(TrainingMode.Review, recent.Exercises[0].Mode);
        Assert.Equal(Now.AddHours(-2), recent.Exercises[1].FinishedAt);
    }

    /// <summary>An unfinished session is not history yet — resuming one is its own feature.</summary>
    [Fact]
    public async Task An_unfinished_session_is_not_listed()
    {
        await using var db = await ArrangeAsync();
        db.Trainings.Add(new Training
        {
            UserId = Owner, DictionaryId = Wool, ChapterId = 1, Mode = TrainingMode.NewBatch,
            CreatedAt = Now.AddMinutes(-5), FinishedAt = null
        });
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Empty(recent.Exercises);
    }

    [Fact]
    public async Task Another_users_exercises_are_not_listed()
    {
        await using var db = await ArrangeAsync();
        await FinishedAsync(db, Stranger, Wool, chapterId: 1, TrainingMode.NewBatch, Now.AddHours(-1));

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Empty(recent.Exercises);
    }

    /// <summary>A book that went private since is no way back at all.</summary>
    [Fact]
    public async Task An_exercise_of_an_invisible_book_is_dropped()
    {
        await using var db = await ArrangeAsync();
        await FinishedAsync(db, Owner, Hidden, chapterId: 3, TrainingMode.NewBatch, Now.AddHours(-1));

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Empty(recent.Exercises);
    }

    [Fact]
    public async Task Exercises_are_capped()
    {
        await using var db = await ArrangeAsync();

        for (var i = 0; i < RecentActivityService.MaxRows + 2; i++)
        {
            await FinishedAsync(db, Owner, Wool, chapterId: null, TrainingMode.NewBatch, Now.AddHours(-i), questions: 1);
            // A distinct scope per session, so the cap is what limits the list, not the dedupe.
            db.Trainings.Local.Last().ChapterId = i % 2 == 0 ? 1 : 2;
            db.Trainings.Local.Last().Mode = i % 3 == 0 ? TrainingMode.Review : TrainingMode.NewBatch;
            await db.SaveChangesAsync();
        }

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.True(recent.Exercises.Count <= RecentActivityService.MaxRows);
    }

    [Fact]
    public async Task A_sorting_visit_carries_its_scope_and_progress()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { UserId = Owner, WordPairId = 1, CreatedAt = Now.AddHours(-1) });
        Visit(db, Wool, chapterId: 1, Now.AddHours(-1));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var sorting = Assert.Single(recent.Sorting);
        Assert.Equal(Wool, sorting.DictionaryId);
        Assert.Equal("Wool", sorting.DictionaryName);
        Assert.Equal(1, sorting.Chapter?.Id);
        Assert.Equal("Holston", sorting.Chapter?.Title);
        Assert.Equal(Now.AddHours(-1), sorting.LastSortedAt);
        Assert.Equal(2, sorting.Total);
        Assert.Equal(1, sorting.Sorted);
    }

    [Fact]
    public async Task A_whole_book_visit_reports_the_books_progress()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.Add(new KnownWord { UserId = Owner, WordPairId = 1, CreatedAt = Now.AddHours(-1) });
        Visit(db, Wool, chapterId: null, Now.AddHours(-1));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var sorting = Assert.Single(recent.Sorting);
        Assert.Null(sorting.Chapter);
        Assert.Equal(4, sorting.Total);
        Assert.Equal(1, sorting.Sorted);
    }

    /// <summary>
    /// The list exists to finish what was started. A scope with nothing left to sort is
    /// finished, so it drops off rather than offering an empty sorter.
    /// </summary>
    [Fact]
    public async Task A_fully_sorted_scope_is_dropped()
    {
        await using var db = await ArrangeAsync();
        db.KnownWords.AddRange(
            new KnownWord { UserId = Owner, WordPairId = 1, CreatedAt = Now.AddHours(-1) },
            new KnownWord { UserId = Owner, WordPairId = 2, CreatedAt = Now.AddHours(-1) });
        Visit(db, Wool, chapterId: 1, Now.AddHours(-1));
        Visit(db, Wool, chapterId: 2, Now.AddHours(-2));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var sorting = Assert.Single(recent.Sorting);
        Assert.Equal(2, sorting.Chapter?.Id);
    }

    [Fact]
    public async Task Sorting_comes_newest_first()
    {
        await using var db = await ArrangeAsync();
        Visit(db, Wool, chapterId: 1, Now.AddHours(-3));
        Visit(db, Wool, chapterId: 2, Now.AddHours(-1));
        Visit(db, Wool, chapterId: null, Now.AddHours(-2));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Equal([2L, null, 1L], recent.Sorting.Select(s => s.Chapter?.Id));
    }

    [Fact]
    public async Task A_visit_to_an_invisible_book_is_dropped()
    {
        await using var db = await ArrangeAsync();
        Visit(db, Hidden, chapterId: 3, Now.AddHours(-1));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        Assert.Empty(recent.Sorting);
    }

    /// <summary>An empty chapter title becomes "Chapter N" in the SPA, so the order must survive.</summary>
    [Fact]
    public async Task An_untitled_chapter_keeps_its_order()
    {
        await using var db = await ArrangeAsync();
        Visit(db, Wool, chapterId: 2, Now.AddHours(-1));
        await db.SaveChangesAsync();

        var recent = await Service(db).GetAsync(Owner, UserRole.User);

        var sorting = Assert.Single(recent.Sorting);
        Assert.Equal(string.Empty, sorting.Chapter?.Title);
        Assert.Equal(1, sorting.Chapter?.Order);
    }
}
