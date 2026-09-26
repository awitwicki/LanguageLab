using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>The three shelves a user sorts a word onto. Mutually exclusive.</summary>
public enum SortStatus
{
    Known = 0,
    Unknown = 1,
    Excluded = 2
}

public sealed record QueueWord(long WordPairId, string Word, string Translation, int Frequency);

public sealed record SortingQueue(IReadOnlyList<QueueWord> Words, int Total, int Sorted, int Remaining);

public sealed record ChapterProgress(long ChapterId, int Total, int Sorted);

/// <summary>How far one sorting scope has got: a whole book's or a single chapter's.</summary>
public sealed record ScopeProgress(int Total, int Sorted);

public sealed record UndoResult(long WordPairId, string Word, string Translation, SortStatus PreviousStatus);

public sealed record RecentWord(long WordPairId, string Word);

public sealed record RecentWords(IReadOnlyList<RecentWord> Known, IReadOnlyList<RecentWord> Unknown);

/// <summary>
/// Where the user was standing when they marked a word: a whole book, or one of its chapters.
/// Passed by the client because the shelves cannot tell afterwards — one word sits in several
/// books. Recorded as a <see cref="SortingVisit"/> so the home screen can offer a way back.
/// </summary>
public sealed record SortingScope(long DictionaryId, long? ChapterId);

/// <summary>
/// The queue of words to sort and the operations on the three shelves.
/// "Sorted" = sitting on any one of them.
/// </summary>
public class WordSortingService
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    private readonly ApplicationDbContext _dbContext;

    public WordSortingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SortingQueue> GetQueueAsync(
        long userId, long dictionaryId, IReadOnlyList<long>? chapterIds, int take)
    {
        take = Math.Clamp(take, 1, MaxTake);

        var scoped = ScopedQuery(dictionaryId, chapterIds);

        var total = await scoped.CountAsync();
        var unsorted = Unsorted(scoped, userId);
        var remaining = await unsorted.CountAsync();

        var words = await unsorted
            // ThenBy id keeps the order deterministic: otherwise a buffer refill
            // could hand out the same word twice or skip another.
            .OrderByDescending(dw => dw.Frequency)
            .ThenBy(dw => dw.WordPairId)
            .Take(take)
            .Select(dw => new QueueWord(dw.WordPairId, dw.WordPair.Word, dw.WordPair.Translation, dw.Frequency))
            .ToListAsync();

        return new SortingQueue(words, total, total - remaining, remaining);
    }

    /// <summary>
    /// How far one scope is sorted — the whole book when <paramref name="chapterId"/> is null.
    /// The per-chapter cousin below walks every chapter of a book; this answers for the one
    /// scope the home screen's list is about.
    /// </summary>
    public async Task<ScopeProgress> CountScopeAsync(long userId, long dictionaryId, long? chapterId)
    {
        var scoped = ScopedQuery(dictionaryId, chapterId == null ? null : [chapterId.Value]);

        var total = await scoped.CountAsync();
        var remaining = await Unsorted(scoped, userId).CountAsync();

        return new ScopeProgress(total, total - remaining);
    }

    public async Task<IReadOnlyList<ChapterProgress>> GetChapterProgressAsync(long userId, long dictionaryId)
    {
        var chapterIds = await _dbContext.Chapters
            .Where(c => c.DictionaryId == dictionaryId)
            .OrderBy(c => c.Order)
            .Select(c => c.Id)
            .ToListAsync();

        var result = new List<ChapterProgress>(chapterIds.Count);

        foreach (var chapterId in chapterIds)
        {
            var scoped = ScopedQuery(dictionaryId, [chapterId]);
            var total = await scoped.CountAsync();
            var remaining = await Unsorted(scoped, userId).CountAsync();
            result.Add(new ChapterProgress(chapterId, total, total - remaining));
        }

        return result;
    }

    /// <summary>
    /// Puts a word on one shelf and removes it from the other two. Exclusivity is
    /// not a formality here: the unique indexes sit on each shelf separately, so
    /// without it a word could end up in both KnownWords and UnknownWords —
    /// and silently drop out of learning for good.
    ///
    /// False for an unknown id or someone else's personal word — the two collapse into the
    /// same refusal so the endpoint can answer 404 either way, the same as
    /// PersonalDictionaryService.RemoveAsync, instead of letting a caller probe which ids
    /// exist by telling ownership and "no such word" apart.
    /// </summary>
    /// <param name="scope">
    /// Where the user was sorting, when the client says. Recorded for the home screen's way
    /// back into unfinished sorting; null leaves no visit behind.
    /// </param>
    public async Task<bool> MarkAsync(
        long userId, long wordPairId, SortStatus status, DateTime nowUtc, SortingScope? scope = null)
    {
        var word = await _dbContext.Words
            .Where(w => w.Id == wordPairId)
            .Select(w => new { w.OwnerId })
            .FirstOrDefaultAsync();

        if (word == null || (word.OwnerId != null && word.OwnerId != userId))
        {
            return false;
        }

        var known = await _dbContext.KnownWords
            .FirstOrDefaultAsync(k => k.UserId == userId && k.WordPairId == wordPairId);
        var unknown = await _dbContext.UnknownWords
            .FirstOrDefaultAsync(u => u.UserId == userId && u.WordPairId == wordPairId);
        var excluded = await _dbContext.ExcludedWords
            .FirstOrDefaultAsync(e => e.UserId == userId && e.WordPairId == wordPairId);

        var alreadyThere = status switch
        {
            SortStatus.Known => known != null,
            SortStatus.Unknown => unknown != null,
            SortStatus.Excluded => excluded != null,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown shelf.")
        };

        // Repeating the same mark is a no-op for the shelves: otherwise the word would jump
        // to the top of the "last 10" column for no reason. The visit at the end still moves —
        // the user is sitting in that scope either way.
        if (alreadyThere)
        {
            await TouchVisitAsync(userId, scope, nowUtc);
            return true;
        }

        if (known != null)
        {
            _dbContext.KnownWords.Remove(known);
        }

        if (unknown != null)
        {
            _dbContext.UnknownWords.Remove(unknown);
        }

        if (excluded != null)
        {
            _dbContext.ExcludedWords.Remove(excluded);
        }

        switch (status)
        {
            case SortStatus.Known:
                _dbContext.KnownWords.Add(new KnownWord
                {
                    UserId = userId, WordPairId = wordPairId, CreatedAt = nowUtc
                });
                break;

            case SortStatus.Unknown:
                _dbContext.UnknownWords.Add(new UnknownWord
                {
                    UserId = userId, WordPairId = wordPairId, CreatedAt = nowUtc
                });
                break;

            case SortStatus.Excluded:
                _dbContext.ExcludedWords.Add(new ExcludedWord
                {
                    UserId = userId, WordPairId = wordPairId, CreatedAt = nowUtc
                });
                break;
        }

        await _dbContext.SaveChangesAsync();
        await TouchVisitAsync(userId, scope, nowUtc);
        return true;
    }

    /// <summary>
    /// Moves the scope's visit to <paramref name="nowUtc"/>, inserting it the first time.
    /// Saved separately from the mark, and on its own terms: a visit is a convenience for the
    /// home screen, so neither a scope the client got wrong nor two tabs inserting the same
    /// row at once may cost the user the mark they actually made.
    /// </summary>
    private async Task TouchVisitAsync(long userId, SortingScope? scope, DateTime nowUtc)
    {
        if (scope == null)
        {
            return;
        }

        // A chapter of another book would send the user somewhere they have never been.
        if (scope.ChapterId is { } chapterId &&
            !await _dbContext.Chapters.AnyAsync(c => c.Id == chapterId && c.DictionaryId == scope.DictionaryId))
        {
            return;
        }

        var visit = await _dbContext.SortingVisits.FirstOrDefaultAsync(v =>
            v.UserId == userId && v.DictionaryId == scope.DictionaryId && v.ChapterId == scope.ChapterId);

        if (visit != null)
        {
            visit.LastSortedAt = nowUtc;
        }
        else
        {
            visit = new SortingVisit
            {
                UserId = userId,
                DictionaryId = scope.DictionaryId,
                ChapterId = scope.ChapterId,
                LastSortedAt = nowUtc
            };

            _dbContext.SortingVisits.Add(visit);
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index rejected a second tab's insert, or the scope named a book that
            // is gone. Detach so the stale entity is not retried by the next save in this scope.
            _dbContext.Entry(visit).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Removes the user's most recent mark, whichever shelf it is on.
    /// Server-side rather than client-side, so it survives a page reload.
    /// </summary>
    public async Task<UndoResult?> UndoAsync(long userId)
    {
        var newestKnown = await _dbContext.KnownWords
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id)
            .FirstOrDefaultAsync();

        var newestUnknown = await _dbContext.UnknownWords
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .FirstOrDefaultAsync();

        var newestExcluded = await _dbContext.ExcludedWords
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync();

        var candidates = new List<(DateTime CreatedAt, long WordPairId, SortStatus Status, object Row)>();

        if (newestKnown != null)
        {
            candidates.Add((newestKnown.CreatedAt, newestKnown.WordPairId, SortStatus.Known, newestKnown));
        }

        if (newestUnknown != null)
        {
            candidates.Add((newestUnknown.CreatedAt, newestUnknown.WordPairId, SortStatus.Unknown, newestUnknown));
        }

        if (newestExcluded != null)
        {
            candidates.Add((newestExcluded.CreatedAt, newestExcluded.WordPairId, SortStatus.Excluded, newestExcluded));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var newest = candidates.MaxBy(c => c.CreatedAt);

        _dbContext.Remove(newest.Row);
        await _dbContext.SaveChangesAsync();

        var word = await _dbContext.Words
            .Where(w => w.Id == newest.WordPairId)
            .Select(w => new { w.Word, w.Translation })
            .SingleAsync();

        return new UndoResult(newest.WordPairId, word.Word, word.Translation, newest.Status);
    }

    /// <summary>
    /// The contents of the left and right columns. Excluded words are not part of
    /// this: they are garbage removal, not a "sorting result".
    /// </summary>
    public async Task<RecentWords> GetRecentAsync(long userId, int take)
    {
        take = Math.Clamp(take, 1, MaxTake);

        var known = await _dbContext.KnownWords
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt).ThenByDescending(k => k.Id)
            .Take(take)
            .Select(k => new RecentWord(k.WordPairId, k.WordPair.Word))
            .ToListAsync();

        var unknown = await _dbContext.UnknownWords
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.Id)
            .Take(take)
            .Select(u => new RecentWord(u.WordPairId, u.WordPair.Word))
            .ToListAsync();

        return new RecentWords(known, unknown);
    }

    /// <summary>
    /// The dictionary's words, narrowed to the chosen chapters when asked. Frequency is
    /// always the book's — even within a chapter it means "how common this word is here at all".
    /// </summary>
    private IQueryable<DictionaryWord> ScopedQuery(long dictionaryId, IReadOnlyList<long>? chapterIds)
    {
        var scoped = _dbContext.DictionaryWords.Where(dw => dw.DictionaryId == dictionaryId);

        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            scoped = scoped.Where(dw => inChapters.Contains(dw.WordPairId));
        }

        return scoped;
    }

    private IQueryable<DictionaryWord> Unsorted(IQueryable<DictionaryWord> scoped, long userId) =>
        scoped.Where(dw =>
            !_dbContext.KnownWords.Any(k => k.UserId == userId && k.WordPairId == dw.WordPairId) &&
            !_dbContext.UnknownWords.Any(u => u.UserId == userId && u.WordPairId == dw.WordPairId) &&
            !_dbContext.ExcludedWords.Any(e => e.UserId == userId && e.WordPairId == dw.WordPairId));
}
