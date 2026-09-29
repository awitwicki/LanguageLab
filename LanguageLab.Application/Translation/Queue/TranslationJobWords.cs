using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Translation.Queue;

/// <summary>A word of a batch, with the keys the job's cursor is made of.</summary>
public sealed record BatchWord(long WordPairId, int Frequency, string Word);

/// <summary>
/// The words a translation job works on — one definition, used both to count a job's Total and
/// to pick its batches: the dictionary's shared words (OwnerId IS NULL) with no WordTranslation
/// in the job's language.
/// </summary>
public static class TranslationJobWords
{
    public static IQueryable<DictionaryWord> Missing(ApplicationDbContext db, long dictionaryId, string language) =>
        db.DictionaryWords.Where(dw =>
            dw.DictionaryId == dictionaryId
            && dw.WordPair.OwnerId == null
            && !dw.WordPair.Translations.Any(t => t.Language == language));

    /// <summary>
    /// The next <paramref name="size"/> missing words after the job's cursor, most frequent
    /// first, ties by WordPairId — the same order the (DictionaryId, Frequency) index serves.
    /// </summary>
    public static Task<List<BatchWord>> NextBatchAsync(
        ApplicationDbContext db, TranslationJob job, int size, CancellationToken cancellationToken)
    {
        var words = Missing(db, job.DictionaryId, job.Language);

        if (job.CursorFrequency is int frequency && job.CursorWordPairId is long wordPairId)
        {
            words = words.Where(dw =>
                dw.Frequency < frequency || (dw.Frequency == frequency && dw.WordPairId > wordPairId));
        }

        return words
            .OrderByDescending(dw => dw.Frequency)
            .ThenBy(dw => dw.WordPairId)
            .Take(size)
            .Select(dw => new BatchWord(dw.WordPairId, dw.Frequency, dw.WordPair.Word))
            .ToListAsync(cancellationToken);
    }
}
