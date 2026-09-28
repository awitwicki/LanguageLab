using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Queue;
using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using BookDictionary = LanguageLab.Domain.Entities.Dictionary;

namespace LanguageLab.Tests;

public class TranslationJobProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly ManualTimeProvider _time = new(Start);

    private ApplicationDbContext Context(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_name, _root)
            .AddInterceptors(interceptors)
            .Options);

    private TranslationJobProcessor Processor(ApplicationDbContext db, IWordBatchTranslator translator) =>
        new(db, translator, _time, NullLogger<TranslationJobProcessor>.Instance);

    private static FakeWordBatchTranslator Answering(params (string Lemma, string Text)[] table) =>
        new(table.ToDictionary(pair => pair.Lemma, pair => pair.Text));

    /// <summary>A dictionary of shared, untranslated words.</summary>
    private async Task SeedDictionaryAsync(long dictionaryId, params (long Id, string Word, int Frequency)[] words)
    {
        await using var db = Context();
        db.Dictionaries.Add(new BookDictionary { Id = dictionaryId, Name = $"Book {dictionaryId}" });
        foreach (var (id, word, frequency) in words)
        {
            db.Words.Add(new WordPair { Id = id, Word = word });
            db.DictionaryWords.Add(new DictionaryWord { DictionaryId = dictionaryId, WordPairId = id, Frequency = frequency });
        }

        await db.SaveChangesAsync();
    }

    /// <summary><paramref name="count"/> words of frequency 1, ids from <paramref name="firstId"/>, spelled prefix0000, prefix0001, …</summary>
    private static (long Id, string Word, int Frequency)[] Many(string prefix, long firstId, int count) =>
        Enumerable.Range(0, count).Select(i => (firstId + i, $"{prefix}{i:D4}", 1)).ToArray();

    private async Task SeedJobAsync(long dictionaryId, string language = "uk", int total = 0)
    {
        await using var db = Context();
        db.TranslationJobs.Add(new TranslationJob
        {
            DictionaryId = dictionaryId, Language = language, Status = TranslationJobStatus.Pending,
            Total = total, CreatedAt = Start.UtcDateTime,
        });
        await db.SaveChangesAsync();
    }

    private async Task<TranslationJob> JobAsync()
    {
        await using var db = Context();
        return await db.TranslationJobs.SingleAsync();
    }

    private async Task<List<WordTranslation>> TranslationsAsync()
    {
        await using var db = Context();
        return await db.WordTranslations.OrderBy(t => t.WordPairId).ToListAsync();
    }

    [Fact]
    public async Task Batch_holds_only_shared_untranslated_words_most_frequent_first()
    {
        await using (var db = Context())
        {
            db.Dictionaries.Add(new BookDictionary { Id = 1, Name = "Wool" });
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
                    Translations = [new WordTranslation { Language = "pl", Text = "polerować" }],
                });
            db.DictionaryWords.AddRange(
                new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 5 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 2, Frequency = 9 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 3, Frequency = 1 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 4, Frequency = 9 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 5, Frequency = 100 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 6, Frequency = 50 },
                new DictionaryWord { DictionaryId = 1, WordPairId = 7, Frequency = 7 });
            await db.SaveChangesAsync();
        }

        await SeedJobAsync(1);
        var translator = Answering();

        await using var context = Context();
        var outcome = await Processor(context, translator).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(ProcessOutcome.Progressed, outcome);
        // The personal "own" and the already-Ukrainian "done" are left out; "polish" is in, since
        // its only translation is Polish. Ties on frequency go by WordPairId.
        Assert.Equal(new[] { "beta", "delta", "polish", "alpha", "gamma" }, translator.Batches.Single());
        Assert.Equal("uk", translator.Targets.Single().Code);
    }

    [Fact]
    public async Task Translations_are_written_as_machine_rows_in_the_jobs_language()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 2), (2, "beta", 1));
        await SeedJobAsync(1, "pl");
        var translator = Answering(("alpha", "  alfa  "), ("beta", "   "));

        await using (var context = Context())
        {
            await Processor(context, translator).ProcessNextAsync(CancellationToken.None);
        }

        var row = Assert.Single(await TranslationsAsync());
        Assert.Equal(1L, row.WordPairId);
        Assert.Equal("pl", row.Language);
        Assert.Equal("alfa", row.Text);
        Assert.Equal(TranslationOrigin.Machine, row.Origin);
        Assert.Equal("pl", translator.Targets.Single().Code);
    }

    [Fact]
    public async Task A_word_translated_meanwhile_is_not_duplicated()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 2), (2, "beta", 1));
        await SeedJobAsync(1);
        var translator = Answering(("alpha", "альфа"), ("beta", "бета"));
        translator.BeforeAnswer = async _ =>
        {
            // A reader's lookup (TranslationService) translates "beta" while the batch is out.
            await using var other = Context();
            other.WordTranslations.Add(new WordTranslation
            {
                WordPairId = 2, Language = "uk", Text = "бета-версія", Origin = TranslationOrigin.Machine,
            });
            await other.SaveChangesAsync();
        };

        await using (var context = Context())
        {
            await Processor(context, translator).ProcessNextAsync(CancellationToken.None);
        }

        Assert.Equal(new[] { "альфа", "бета-версія" }, (await TranslationsAsync()).Select(t => t.Text));
    }

    [Fact]
    public async Task A_word_the_model_did_not_answer_is_passed_over()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 3), (2, "beta", 2));
        await SeedJobAsync(1, total: 2);
        var translator = Answering(("alpha", "альфа"));

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Progressed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        var job = await JobAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(2, job.Done);
        Assert.Equal<int?>(2, job.CursorFrequency);
        Assert.Equal<long?>(2, job.CursorWordPairId);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Equal<DateTime?>(Start.UtcDateTime, job.LastProcessedAt);

        _time.Advance(TimeSpan.FromSeconds(1));
        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Completed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        // "beta" is still untranslated but behind the cursor: not asked for again in this pass.
        Assert.Single(translator.Batches);
        Assert.Equal(TranslationJobStatus.Completed, (await JobAsync()).Status);
    }

    [Fact]
    public async Task An_empty_batch_completes_the_job()
    {
        await using (var db = Context())
        {
            db.Dictionaries.Add(new BookDictionary { Id = 1, Name = "Wool" });
            db.Words.Add(TestWords.Pair(1, "done", "зроблено"));
            db.DictionaryWords.Add(new DictionaryWord { DictionaryId = 1, WordPairId = 1, Frequency = 4 });
            await db.SaveChangesAsync();
        }

        await SeedJobAsync(1);
        var translator = Answering();

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Completed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        Assert.Empty(translator.Batches);
        var job = await JobAsync();
        Assert.Equal(TranslationJobStatus.Completed, job.Status);
        Assert.Equal<DateTime?>(Start.UtcDateTime, job.LastProcessedAt);
    }

    [Fact]
    public async Task A_quota_refusal_changes_nothing_and_carries_retry_after()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedJobAsync(1, total: 1);
        var translator = Answering(("alpha", "альфа"));
        translator.Failures.Enqueue(new LlmQuotaException("quota", TimeSpan.FromSeconds(90)));

        ProcessOutcome outcome;
        await using (var context = Context())
        {
            outcome = await Processor(context, translator).ProcessNextAsync(CancellationToken.None);
        }

        Assert.Equal(new ProcessOutcome(ProcessOutcomeKind.QuotaExceeded, TimeSpan.FromSeconds(90)), outcome);
        var job = await JobAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(0, job.Done);
        Assert.Null(job.CursorWordPairId);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.LastProcessedAt);
        Assert.Empty(await TranslationsAsync());
    }

    [Fact]
    public async Task The_fifth_unavailable_batch_in_a_row_fails_the_job()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedJobAsync(1, total: 1);
        var translator = Answering(("alpha", "альфа"));
        for (var i = 0; i < 5; i++)
        {
            translator.Failures.Enqueue(new LlmUnavailableException("down"));
        }

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await using (var context = Context())
            {
                Assert.Equal(ProcessOutcome.Failed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
            }

            var job = await JobAsync();
            Assert.Equal(TranslationJobStatus.Pending, job.Status);
            Assert.Equal(attempt, job.ConsecutiveFailures);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Failed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        var failed = await JobAsync();
        Assert.Equal(TranslationJobStatus.Failed, failed.Status);
        Assert.Equal(5, failed.ConsecutiveFailures);
        Assert.Equal<DateTime?>(Start.AddMinutes(4).UtcDateTime, failed.LastProcessedAt);

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Idle, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        Assert.Empty(await TranslationsAsync());
    }

    [Fact]
    public async Task A_successful_batch_resets_the_failure_count()
    {
        await SeedDictionaryAsync(1, Many("w", 1, 201));
        await SeedJobAsync(1, total: 201);
        var translator = Answering();

        async Task<ProcessOutcome> NextAsync()
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await using var context = Context();
            return await Processor(context, translator).ProcessNextAsync(CancellationToken.None);
        }

        for (var i = 0; i < 4; i++)
        {
            translator.Failures.Enqueue(new LlmUnavailableException("down"));
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ProcessOutcome.Failed, await NextAsync());
        }

        Assert.Equal(4, (await JobAsync()).ConsecutiveFailures);

        Assert.Equal(ProcessOutcome.Progressed, await NextAsync());
        Assert.Equal(0, (await JobAsync()).ConsecutiveFailures);

        for (var i = 0; i < 4; i++)
        {
            translator.Failures.Enqueue(new LlmUnavailableException("down"));
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ProcessOutcome.Failed, await NextAsync());
        }

        var job = await JobAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(4, job.ConsecutiveFailures);
    }

    [Fact]
    public async Task Two_jobs_are_served_in_turn()
    {
        await SeedDictionaryAsync(1, Many("a", 1, 201));
        await SeedDictionaryAsync(2, Many("b", 1001, 201));
        await SeedJobAsync(1);
        await SeedJobAsync(2);
        var translator = Answering();

        var outcomes = new List<ProcessOutcome>();
        for (var i = 0; i < 7; i++)
        {
            await using (var context = Context())
            {
                outcomes.Add(await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
            }

            _time.Advance(TimeSpan.FromSeconds(1));
        }

        // Job 2 has never been processed when job 1's first batch is done, so it goes next even
        // though job 1 has more to do — one large dictionary never blocks another.
        Assert.Equal(new[] { 'a', 'b', 'a', 'b' }, translator.Batches.Select(batch => batch[0][0]));
        Assert.Equal(
            new[]
            {
                ProcessOutcome.Progressed, ProcessOutcome.Progressed, ProcessOutcome.Progressed,
                ProcessOutcome.Progressed, ProcessOutcome.Completed, ProcessOutcome.Completed,
                ProcessOutcome.Idle,
            },
            outcomes);
    }

    [Fact]
    public async Task An_unconfigured_translator_idles_without_a_call()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedJobAsync(1);
        var translator = Answering(("alpha", "альфа"));
        translator.IsConfigured = false;

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Idle, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        Assert.Empty(translator.Batches);
        Assert.Null((await JobAsync()).LastProcessedAt);
    }

    [Fact]
    public async Task No_pending_job_is_idle()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await using (var db = Context())
        {
            db.TranslationJobs.Add(new TranslationJob
            {
                DictionaryId = 1, Language = "uk", Status = TranslationJobStatus.Completed, CreatedAt = Start.UtcDateTime,
            });
            await db.SaveChangesAsync();
        }

        var translator = Answering(("alpha", "альфа"));

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Idle, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        Assert.Empty(translator.Batches);
    }

    [Fact]
    public async Task Batches_never_exceed_two_hundred()
    {
        await SeedDictionaryAsync(1, Many("w", 1, 450));
        await SeedJobAsync(1, total: 450);
        var translator = Answering();

        ProcessOutcome outcome;
        do
        {
            await using (var context = Context())
            {
                outcome = await Processor(context, translator).ProcessNextAsync(CancellationToken.None);
            }

            _time.Advance(TimeSpan.FromSeconds(1));
        }
        while (outcome == ProcessOutcome.Progressed);

        Assert.Equal(ProcessOutcome.Completed, outcome);
        Assert.Equal(new[] { 200, 200, 50 }, translator.Batches.Select(batch => batch.Count));
        Assert.Equal(450, (await JobAsync()).Done);
    }

    [Fact]
    public async Task A_conflict_on_save_falls_back_to_one_row_at_a_time()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 3), (2, "beta", 2), (3, "gamma", 1));
        await SeedJobAsync(1, total: 3);
        var translator = Answering(("alpha", "альфа"), ("beta", "бета"), ("gamma", "гамма"));
        // Postgres refuses "beta" on the unique (WordPairId, Language) index: another writer got
        // there between the processor's check and its save.
        var conflict = new FailingSaveInterceptor(context => context.ChangeTracker.Entries<WordTranslation>()
            .Any(e => e.State == EntityState.Added && e.Entity.WordPairId == 2));

        await using (var context = Context(conflict))
        {
            Assert.Equal(ProcessOutcome.Progressed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        // Once for the whole batch, once for "beta" alone.
        Assert.Equal(2, conflict.Failures);
        Assert.Equal(new[] { 1L, 3L }, (await TranslationsAsync()).Select(t => t.WordPairId));
        var job = await JobAsync();
        Assert.Equal(3, job.Done);
        Assert.Equal<long?>(3, job.CursorWordPairId);
        Assert.Equal<DateTime?>(Start.UtcDateTime, job.LastProcessedAt);
    }

    [Fact]
    public async Task A_job_in_a_language_off_the_catalog_fails()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedJobAsync(1, "ru");
        var translator = Answering(("alpha", "альфа"));

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Failed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        Assert.Empty(translator.Batches);
        Assert.Equal(TranslationJobStatus.Failed, (await JobAsync()).Status);
    }

    [Fact]
    public async Task Cancellation_is_not_counted_as_a_failure()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedJobAsync(1);
        var translator = Answering(("alpha", "альфа"));
        translator.BeforeAnswer = _ => Task.FromException(new OperationCanceledException());

        await using (var context = Context())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        var job = await JobAsync();
        Assert.Equal(TranslationJobStatus.Pending, job.Status);
        Assert.Equal(0, job.ConsecutiveFailures);
        Assert.Null(job.LastProcessedAt);
    }

    /// <summary>
    /// An exception the translator's contract does not document (a bug, or a provider surprise
    /// not yet mapped to LlmUnavailableException) must still count as a failure and move
    /// LastProcessedAt on — otherwise the job never leaves "never processed" and the round-robin
    /// pick keeps giving it every turn, starving every other job forever.
    /// </summary>
    [Fact]
    public async Task An_unexpected_exception_counts_as_a_failure_and_the_next_job_still_gets_its_turn()
    {
        await SeedDictionaryAsync(1, (1, "alpha", 1));
        await SeedDictionaryAsync(2, (2, "beta", 1));
        await SeedJobAsync(1, total: 1);
        await SeedJobAsync(2, total: 1);
        var translator = Answering(("beta", "бета"));
        var attempt = 0;
        translator.BeforeAnswer = _ =>
        {
            attempt++;
            return attempt == 1 ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        };

        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Failed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        await using (var db = Context())
        {
            var job1 = await db.TranslationJobs.SingleAsync(j => j.DictionaryId == 1);
            Assert.Equal(1, job1.ConsecutiveFailures);
            Assert.Equal<DateTime?>(Start.UtcDateTime, job1.LastProcessedAt);
        }

        _time.Advance(TimeSpan.FromMinutes(1));
        await using (var context = Context())
        {
            Assert.Equal(ProcessOutcome.Progressed, await Processor(context, translator).ProcessNextAsync(CancellationToken.None));
        }

        var row = Assert.Single(await TranslationsAsync());
        Assert.Equal(2L, row.WordPairId);
    }
}
