using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Application.Translation.Queue;

/// <summary>
/// One iteration of the translation worker: exactly one batch of one job. Round-robin — the
/// pending job processed longest ago goes next, a never-processed one first — so one large
/// import never blocks a small one. Writes Machine translations only where none exists in the
/// job's language, so a hand-made or earlier one always wins. Scoped: one per worker iteration.
/// Logs ids, codes and counts only — never words or translations.
/// </summary>
public sealed class TranslationJobProcessor
{
    /// <summary>Lemmas per provider call; IWordBatchTranslator does not split, so the caller sizes it.</summary>
    public const int BatchSize = 200;

    /// <summary>Unavailable batches in a row after which a job is given up until the next enqueue.</summary>
    public const int MaxConsecutiveFailures = 5;

    private readonly ApplicationDbContext _db;
    private readonly IWordBatchTranslator _translator;
    private readonly TimeProvider _time;
    private readonly ILogger<TranslationJobProcessor> _logger;

    public TranslationJobProcessor(
        ApplicationDbContext db,
        IWordBatchTranslator translator,
        TimeProvider time,
        ILogger<TranslationJobProcessor> logger)
    {
        _db = db;
        _translator = translator;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<ProcessOutcome> ProcessNextAsync(CancellationToken cancellationToken)
    {
        if (!_translator.IsConfigured)
        {
            return ProcessOutcome.Idle;
        }

        // Postgres sorts NULL last in an ascending ORDER BY, so "never processed first" has to
        // be its own key rather than left to how nulls happen to sort.
        var job = await _db.TranslationJobs
            .Where(j => j.Status == TranslationJobStatus.Pending)
            .OrderBy(j => j.LastProcessedAt != null)
            .ThenBy(j => j.LastProcessedAt)
            .ThenBy(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
        {
            return ProcessOutcome.Idle;
        }

        var language = LearnerLanguages.Find(job.Language);

        if (language == null)
        {
            job.Status = TranslationJobStatus.Failed;
            job.LastProcessedAt = Now;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Translation job {DictionaryId}/{Language} failed: the language is not in the catalog",
                job.DictionaryId, job.Language);
            return ProcessOutcome.Failed;
        }

        var batch = await TranslationJobWords.NextBatchAsync(_db, job, BatchSize, cancellationToken);

        if (batch.Count == 0)
        {
            job.Status = TranslationJobStatus.Completed;
            job.LastProcessedAt = Now;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Translation job {DictionaryId}/{Language} completed: {Done} of {Total}",
                job.DictionaryId, job.Language, job.Done, job.Total);
            return ProcessOutcome.Completed;
        }

        if (job.CursorWordPairId == null && job.ConsecutiveFailures == 0)
        {
            _logger.LogInformation(
                "Translation job {DictionaryId}/{Language} started: {Total} words to translate",
                job.DictionaryId, job.Language, job.Total);
        }

        IReadOnlyDictionary<string, string> answer;

        try
        {
            answer = await _translator.TranslateAsync(batch.Select(w => w.Word).ToList(), language, cancellationToken);
        }
        catch (LlmQuotaException e)
        {
            // The quota is the provider's, shared by every job: the worker pauses as a whole,
            // and this job keeps its turn.
            return ProcessOutcome.Quota(e.RetryAfter);
        }
        catch (LlmUnavailableException)
        {
            job.ConsecutiveFailures++;
            job.LastProcessedAt = Now;

            if (job.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                job.Status = TranslationJobStatus.Failed;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Translation job {DictionaryId}/{Language}: batch unavailable, {Failures} in a row, job now {Status}",
                job.DictionaryId, job.Language, job.ConsecutiveFailures, job.Status);
            return ProcessOutcome.Failed;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Outside the translator's documented contract (LlmQuotaException, LlmUnavailableException).
            // Still counted like an unavailable batch: leaving LastProcessedAt untouched would keep
            // this job first in the round-robin pick forever, starving every other job.
            job.ConsecutiveFailures++;
            job.LastProcessedAt = Now;

            if (job.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                job.Status = TranslationJobStatus.Failed;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Translation job {DictionaryId}/{Language}: batch failed with {ExceptionType}, {Failures} in a row, job now {Status}",
                job.DictionaryId, job.Language, e.GetType().Name, job.ConsecutiveFailures, job.Status);
            return ProcessOutcome.Failed;
        }

        var rows = await NewTranslationsAsync(job.Language, batch, answer, cancellationToken);
        _db.WordTranslations.AddRange(rows.Select(row => Row(job.Language, row)));
        MoveCursor(job, batch);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await SaveOneByOneAsync(job.Id, job.Language, batch, rows, cancellationToken);
        }

        return ProcessOutcome.Progressed;
    }

    /// <summary>
    /// The batch's words the answer covers and that still have no translation in the language —
    /// the interactive TranslationService may have written one while the batch was out. Keys are
    /// matched to WordPair.Word; text is trimmed and a blank one skipped.
    /// </summary>
    private async Task<List<(long WordPairId, string Text)>> NewTranslationsAsync(
        string language,
        IReadOnlyList<BatchWord> batch,
        IReadOnlyDictionary<string, string> answer,
        CancellationToken cancellationToken)
    {
        var ids = batch.Select(w => w.WordPairId).ToList();
        var translated = (await _db.WordTranslations
                .Where(t => t.Language == language && ids.Contains(t.WordPairId))
                .Select(t => t.WordPairId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var rows = new List<(long WordPairId, string Text)>();

        foreach (var word in batch)
        {
            if (translated.Contains(word.WordPairId)
                || !answer.TryGetValue(word.Word, out var text)
                || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            rows.Add((word.WordPairId, text.Trim()));
        }

        return rows;
    }

    /// <summary>
    /// A writer beat NewTranslationsAsync's check to one of the rows and the unique
    /// (WordPairId, Language) index refused the batch. Theirs stands; ours go in one row per save,
    /// skipping the ones that conflict, and the cursor moves on as usual.
    /// </summary>
    private async Task SaveOneByOneAsync(
        long jobId,
        string language,
        IReadOnlyList<BatchWord> batch,
        List<(long WordPairId, string Text)> rows,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        var job = await _db.TranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job == null)
        {
            // The dictionary was deleted meanwhile, and its job with it.
            return;
        }

        foreach (var row in rows)
        {
            var entity = Row(language, row);
            _db.WordTranslations.Add(entity);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(entity).State = EntityState.Detached;
            }
        }

        MoveCursor(job, batch);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static WordTranslation Row(string language, (long WordPairId, string Text) row) => new()
    {
        WordPairId = row.WordPairId,
        Language = language,
        Text = row.Text,
        Origin = TranslationOrigin.Machine,
    };

    /// <summary>Past the batch's last word, translated or not: a pass never asks for a word twice.</summary>
    private void MoveCursor(TranslationJob job, IReadOnlyList<BatchWord> batch)
    {
        var last = batch[^1];
        job.CursorFrequency = last.Frequency;
        job.CursorWordPairId = last.WordPairId;
        job.Done += batch.Count;
        job.ConsecutiveFailures = 0;
        job.LastProcessedAt = Now;
    }
}
