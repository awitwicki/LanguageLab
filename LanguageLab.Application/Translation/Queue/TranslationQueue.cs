using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Translation.Queue;

/// <summary>
/// Creates or re-arms the (dictionary, language) TranslationJob and wakes the worker. A pending
/// job is left as it is; a completed or failed one starts a fresh pass from the most frequent
/// word, with Total recounted. Personal dictionaries are never queued — their words belong to
/// their owner, not to the shared vocabulary.
/// </summary>
public sealed class TranslationQueue : ITranslationQueue
{
    private readonly ApplicationDbContext _db;
    private readonly TranslationQueueSignal _signal;
    private readonly TimeProvider _time;

    public TranslationQueue(ApplicationDbContext db, TranslationQueueSignal signal, TimeProvider time)
    {
        _db = db;
        _signal = signal;
        _time = time;
    }

    public async Task EnqueueAsync(long dictionaryId, LearnerLanguage language, CancellationToken cancellationToken)
    {
        var isPersonal = await _db.Dictionaries
            .Where(d => d.Id == dictionaryId)
            .Select(d => (bool?)d.IsPersonal)
            .FirstOrDefaultAsync(cancellationToken);

        if (isPersonal is not false)
        {
            return;
        }

        var code = language.Code;
        var job = await _db.TranslationJobs
            .FirstOrDefaultAsync(j => j.DictionaryId == dictionaryId && j.Language == code, cancellationToken);

        if (job is { Status: TranslationJobStatus.Pending })
        {
            return;
        }

        var total = await TranslationJobWords.Missing(_db, dictionaryId, code).CountAsync(cancellationToken);

        if (job == null)
        {
            job = new TranslationJob
            {
                DictionaryId = dictionaryId,
                Language = code,
                Status = TranslationJobStatus.Pending,
                Total = total,
                CreatedAt = _time.GetUtcNow().UtcDateTime,
            };
            _db.TranslationJobs.Add(job);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another request created the same job between our lookup and our insert, and
                // the unique (DictionaryId, Language) index refused ours. Theirs is pending — the
                // same as finding it there — and they have woken the worker.
                _db.Entry(job).State = EntityState.Detached;
                return;
            }
        }
        else
        {
            job.Status = TranslationJobStatus.Pending;
            job.Total = total;
            job.Done = 0;
            job.CursorFrequency = null;
            job.CursorWordPairId = null;
            job.ConsecutiveFailures = 0;
            // Null sorts first in the worker's pick, so a re-armed job is served promptly.
            job.LastProcessedAt = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _signal.Wake();
    }
}
