using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// Deleting a dictionary, with the cleanup the endpoint used to leave out. Shared WordPair rows
/// are global on purpose — they carry the shelves across books — but a row that belonged only to
/// the deleted dictionary and that nobody has touched is nothing but residue, and residue from a
/// bad import used to be unreachable forever.
/// A personal dictionary is never deleted this way: it is somebody's own word list, and it is
/// invisible to everyone else, admins included.
/// </summary>
public class DictionaryDeletionService
{
    private readonly ApplicationDbContext _dbContext;

    public DictionaryDeletionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> DeleteAsync(long dictionaryId)
    {
        var dictionary = await _dbContext.Dictionaries
            .FirstOrDefaultAsync(d => d.Id == dictionaryId && !d.IsPersonal);

        if (dictionary == null)
        {
            return false;
        }

        // Which shared words this dictionary holds, before the join rows go.
        var wordIds = await _dbContext.DictionaryWords
            .Where(dw => dw.DictionaryId == dictionaryId)
            .Select(dw => dw.WordPairId)
            .ToListAsync();

        // Cascades take down Chapters, ChapterWords and DictionaryWords in the database; the
        // InMemory provider cascades nothing, so the join rows are removed by hand too — same
        // teardown as PersonalDictionaryService.RemoveAsync.
        _dbContext.DictionaryWords.RemoveRange(
            _dbContext.DictionaryWords.Where(dw => dw.DictionaryId == dictionaryId));
        _dbContext.Dictionaries.Remove(dictionary);
        await _dbContext.SaveChangesAsync();

        await RemoveOrphansAsync(wordIds);
        return true;
    }

    /// <summary>Every non-personal dictionary a user owns. Returns how many went.</summary>
    public async Task<int> DeleteOwnedAsync(long ownerId)
    {
        var ids = await _dbContext.Dictionaries
            .Where(d => d.OwnerId == ownerId && !d.IsPersonal)
            .Select(d => d.Id)
            .ToListAsync();

        var deleted = 0;

        foreach (var id in ids)
        {
            if (await DeleteAsync(id))
            {
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>
    /// A shared word is residue when no dictionary holds it any more and no user has ever
    /// shelved it, trained it or been asked it. The InMemory provider cascades nothing, so the
    /// join rows are checked rather than assumed gone.
    /// </summary>
    private async Task RemoveOrphansAsync(IReadOnlyList<long> wordIds)
    {
        if (wordIds.Count == 0)
        {
            return;
        }

        var orphans = await _dbContext.Words
            .Where(w => wordIds.Contains(w.Id) && w.OwnerId == null)
            .Where(w => !_dbContext.DictionaryWords.Any(dw => dw.WordPairId == w.Id))
            .Where(w => !_dbContext.KnownWords.Any(k => k.WordPairId == w.Id))
            .Where(w => !_dbContext.UnknownWords.Any(u => u.WordPairId == w.Id))
            .Where(w => !_dbContext.ExcludedWords.Any(e => e.WordPairId == w.Id))
            .Where(w => !_dbContext.WordProgresses.Any(p => p.WordPairId == w.Id))
            .Where(w => !_dbContext.TrainingQuestions.Any(q => q.WordPairId == w.Id))
            .ToListAsync();

        if (orphans.Count == 0)
        {
            return;
        }

        _dbContext.Words.RemoveRange(orphans);
        await _dbContext.SaveChangesAsync();
    }
}
