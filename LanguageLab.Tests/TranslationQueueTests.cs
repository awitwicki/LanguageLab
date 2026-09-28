using LanguageLab.Application.Translation.Queue;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using BookDictionary = LanguageLab.Domain.Entities.Dictionary;

namespace LanguageLab.Tests;

public class TranslationQueueTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;
    private static readonly LearnerLanguage Pl = LearnerLanguages.Find("pl")!;

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly TranslationQueueSignal _signal = new();

    private ApplicationDbContext Context(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_name, _root)
            .AddInterceptors(interceptors)
            .Options);

    private TranslationQueue Queue(ApplicationDbContext db) => new(db, _signal, _time);

    private Task<bool> WokenAsync() => _signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);

    /// <summary>
    /// Dictionary 1 holds five shared words with no "uk" translation (alpha, beta, gamma, delta,
    /// and polish, which is translated into "pl" only), one shared word already in "uk" (done)
    /// and one personal word (own). Dictionary 2 is someone's personal list.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var db = Context();
        db.Dictionaries.AddRange(
            new BookDictionary { Id = 1, Name = "Wool" },
            new BookDictionary { Id = 2, Name = "My words", IsPersonal = true, OwnerId = 9 });
        db.Words.AddRange(
            new WordPair { Id = 1, Word = "alpha" },
            new WordPair { Id = 2, Word = "beta" },
            new WordPair { Id = 3, Word = "gamma" },
            new WordPair { Id = 4, Word = "delta" },
            new WordPair { Id = 5, Word = "own", OwnerId = 9 },
            TestWords.Pair(6, "done", "зроблено"),
            new WordPair
            {
                Id = 7, Word = "polish",
                Translations = [new WordTranslation { Language = "pl", Text = "полірувати" }],
            });
        db.DictionaryWords.AddRange(
            new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 5 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 2, Frequency = 9 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 3, Frequency = 1 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 4, Frequency = 9 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 5, Frequency = 100 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 6, Frequency = 50 },
            new DictionaryWord { DictionaryId = 1, WordPairId = 7, Frequency = 7 },
            new DictionaryWord { DictionaryId = 2, WordPairId = 5, Frequency = 0 });
        await db.SaveChangesAsync();
    }

    private async Task<List<TranslationJob>> JobsAsync()
    {
        await using var db = Context();
        return await db.TranslationJobs.OrderBy(j => j.Language).ToListAsync();
    }

    [Fact]
    public async Task Creates_a_pending_job_counting_the_missing_shared_words()
    {
        await SeedAsync();

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(1, Uk, CancellationToken.None);
        }

        var job = Assert.Single(await JobsAsync());
        Assert.Equal(1L, job.DictionaryId);
        Assert.Equal("uk", job.Language);
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(5, job.Total);
        Assert.Equal(0, job.Done);
        Assert.Null(job.CursorFrequency);
        Assert.Null(job.CursorWordPairId);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.LastProcessedAt);
        Assert.Equal(Start.UtcDateTime, job.CreatedAt);
        Assert.True(await WokenAsync());
    }

    [Fact]
    public async Task Each_language_is_a_job_of_its_own()
    {
        await SeedAsync();

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(1, Uk, CancellationToken.None);
            await Queue(db).EnqueueAsync(1, Pl, CancellationToken.None);
        }

        var jobs = await JobsAsync();
        Assert.Equal(new[] { "pl", "uk" }, jobs.Select(j => j.Language));
        // "pl" misses alpha, beta, gamma, delta and done; "uk" misses alpha, beta, gamma, delta and polish.
        Assert.Equal(new[] { 5, 5 }, jobs.Select(j => j.Total));
    }

    [Fact]
    public async Task A_pending_job_is_left_alone()
    {
        await SeedAsync();
        await using (var db = Context())
        {
            db.TranslationJobs.Add(new TranslationJob
            {
                DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Pending, Total = 99, Done = 3,
                CursorFrequency = 9, CursorWordPairId = 4, CreatedAt = Start.UtcDateTime,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(1, Uk, CancellationToken.None);
        }

        var job = Assert.Single(await JobsAsync());
        Assert.Equal(99, job.Total);
        Assert.Equal(3, job.Done);
        Assert.Equal<long?>(4, job.CursorWordPairId);
        Assert.False(await WokenAsync());
    }

    [Theory]
    [InlineData(TranslationJobStatus.Completed)]
    [InlineData(TranslationJobStatus.Failed)]
    public async Task A_finished_job_is_re_armed(TranslationJobStatus finished)
    {
        await SeedAsync();
        var created = Start.AddDays(-1).UtcDateTime;
        await using (var db = Context())
        {
            db.TranslationJobs.Add(new TranslationJob
            {
                DictionaryId = 1, Language = "uk", Status = finished, Total = 99, Done = 7,
                CursorFrequency = 1, CursorWordPairId = 3, ConsecutiveFailures = 3,
                LastProcessedAt = Start.AddHours(-1).UtcDateTime, CreatedAt = created,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(1, Uk, CancellationToken.None);
        }

        var job = Assert.Single(await JobsAsync());
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(5, job.Total);
        Assert.Equal(0, job.Done);
        Assert.Null(job.CursorFrequency);
        Assert.Null(job.CursorWordPairId);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.LastProcessedAt);
        Assert.Equal(created, job.CreatedAt);
        Assert.True(await WokenAsync());
    }

    [Fact]
    public async Task A_personal_dictionary_is_never_queued()
    {
        await SeedAsync();

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(2, Uk, CancellationToken.None);
        }

        Assert.Empty(await JobsAsync());
        Assert.False(await WokenAsync());
    }

    [Fact]
    public async Task A_missing_dictionary_is_a_no_op()
    {
        await SeedAsync();

        await using (var db = Context())
        {
            await Queue(db).EnqueueAsync(404, Uk, CancellationToken.None);
        }

        Assert.Empty(await JobsAsync());
        Assert.False(await WokenAsync());
    }

    /// <summary>
    /// Two first enqueues race: the other request inserts the job between this one's lookup and
    /// its save, and Postgres refuses the second row on the unique index. The winner's job stands.
    /// </summary>
    [Fact]
    public async Task A_concurrent_insert_of_the_same_job_counts_as_pending()
    {
        await SeedAsync();
        var raced = false;
        var race = new FailingSaveInterceptor(context =>
        {
            if (raced || !context.ChangeTracker.Entries<TranslationJob>().Any(e => e.State == EntityState.Added))
            {
                return false;
            }

            raced = true;
            using var winner = Context();
            winner.TranslationJobs.Add(new TranslationJob
            {
                DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Pending, Total = 5,
                CreatedAt = Start.UtcDateTime,
            });
            winner.SaveChanges();
            return true;
        });

        await using (var db = Context(race))
        {
            await Queue(db).EnqueueAsync(1, Uk, CancellationToken.None);
        }

        Assert.Equal(1, race.Failures);
        Assert.Single(await JobsAsync());
    }
}
