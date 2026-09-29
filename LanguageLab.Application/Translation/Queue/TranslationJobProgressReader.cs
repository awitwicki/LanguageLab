using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Translation.Queue;

public sealed record TranslationProgress(int Done, int Total);

/// <summary>
/// Reads a (dictionary, language) TranslationJob's progress for display. Null unless the job is
/// Pending with real work left — a job the same request just re-armed with nothing missing, or a
/// Completed/Failed one, is not worth showing as "translating" — and unless a translator is
/// configured: an unconfigured one never processes a batch (TranslationJobProcessor.ProcessNextAsync
/// returns Idle without touching the job), so a job it will never advance is not "translating"
/// either — final review, finding 2.
/// </summary>
public sealed class TranslationJobProgressReader
{
    private readonly ApplicationDbContext _db;
    private readonly IWordBatchTranslator _translator;

    public TranslationJobProgressReader(ApplicationDbContext db, IWordBatchTranslator translator)
    {
        _db = db;
        _translator = translator;
    }

    public async Task<TranslationProgress?> GetAsync(long dictionaryId, string language)
    {
        if (!_translator.IsConfigured)
        {
            return null;
        }

        var job = await _db.TranslationJobs
            .Where(j => j.DictionaryId == dictionaryId && j.Language == language)
            .Select(j => new { j.Status, j.Done, j.Total })
            .FirstOrDefaultAsync();

        return job is { Status: TranslationJobStatus.Pending } && job.Total > job.Done
            ? new TranslationProgress(job.Done, job.Total)
            : null;
    }
}
