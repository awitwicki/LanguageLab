using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>A batch candidate word with its frequency in the scope being trained.</summary>
public sealed record Candidate(long WordPairId, string Word, string Translation, int Frequency);

/// <summary>
/// Whether a scope can be reviewed right now: how many words wait, and — when none do —
/// when the next one comes due. Drives the chapter row's "Review" / "next review tomorrow".
/// </summary>
public sealed record ReviewAvailability(int DueCount, DateTime? NextDueAt)
{
    public static ReviewAvailability None => new(0, null);
}

/// <summary>
/// Decides which words to show. The "don't learn what I already know" rule lives here:
/// a word enters a new batch only if it is in this dictionary, has a translation, is marked
/// by the user as "want to learn", is not marked as known, is not excluded by the user and has never been trained.
/// </summary>
public class WordSelectionService
{
    public const int NewBatchSize = 5;
    public const int ReviewSessionSize = 20;
    public const int DistractorPoolSize = 60;
    public const int MaxCandidates = 30;

    private readonly ApplicationDbContext _dbContext;

    public WordSelectionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// The scope's most frequent learnable words by that scope's frequency: per chapter the sum of
    /// ChapterWord.Count over the chosen chapters, per book DictionaryWord.Frequency. The order is
    /// deterministic (frequency, word, id) because the start screen's preview must match the batch.
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> GetCandidatesAsync(
        long userId, long dictionaryId, IReadOnlyList<long>? chapterIds, int take)
    {
        take = Math.Clamp(take, 1, MaxCandidates);
        var learnable = LearnableQuery(userId, dictionaryId, chapterIds);

        var ranked = chapterIds is { Count: > 0 }
            ? learnable.Join(
                _dbContext.ChapterWords
                    .Where(cw => chapterIds.Contains(cw.ChapterId))
                    .GroupBy(cw => cw.WordPairId)
                    .Select(g => new { WordPairId = g.Key, Frequency = g.Sum(cw => cw.Count) }),
                w => w.Id,
                f => f.WordPairId,
                (w, f) => new { w.Id, w.Word, w.Translation, f.Frequency })
            : learnable.Join(
                _dbContext.DictionaryWords.Where(dw => dw.DictionaryId == dictionaryId),
                w => w.Id,
                dw => dw.WordPairId,
                (w, dw) => new { w.Id, w.Word, w.Translation, dw.Frequency });

        var rows = await ranked
            .OrderByDescending(x => x.Frequency)
            .ThenBy(x => x.Word)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync();

        return rows.Select(x => new Candidate(x.Id, x.Word, x.Translation, x.Frequency)).ToList();
    }

    /// <summary>The first size candidates as WordPair, in the same order. QuestionQueueBuilder shuffles the quiz order.</summary>
    public async Task<IReadOnlyList<WordPair>> GetNewBatchAsync(
        long userId, long dictionaryId, int size, IReadOnlyList<long>? chapterIds = null)
    {
        var candidates = await GetCandidatesAsync(userId, dictionaryId, chapterIds, size);

        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.Select(c => c.WordPairId).ToList();

        var words = await _dbContext.Words
            .Where(w => ids.Contains(w.Id))
            .ToListAsync();

        // Loading by a set of ids does not preserve order — restore the candidates' order.
        return ids.Select(id => words.First(w => w.Id == id)).ToList();
    }

    /// <summary>
    /// The subset of ids still learnable in the scope, in ids order, without duplicates. Foreign or
    /// vanished ids (crossed out in another tab, already trained) are dropped silently — a race, not a client error.
    /// </summary>
    public async Task<IReadOnlyList<WordPair>> GetLearnableByIdsAsync(
        long userId, long dictionaryId, IReadOnlyList<long>? chapterIds, IReadOnlyList<long> ids)
    {
        var wanted = ids.Distinct().ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var words = await LearnableQuery(userId, dictionaryId, chapterIds)
            .Where(w => wanted.Contains(w.Id))
            .ToListAsync();

        return wanted
            .Select(id => words.FirstOrDefault(w => w.Id == id))
            .OfType<WordPair>()
            .ToList();
    }

    /// <summary>
    /// Overdue, unlearned words, earliest due first. Without a scope this is the global
    /// review; with one it is a chapter's (or a book's) own review — the same words, just
    /// filtered, so the Leitner schedule is honoured either way.
    /// </summary>
    public async Task<IReadOnlyList<WordPair>> GetDueWordsAsync(
        long userId, DateTime nowUtc, int size, long? dictionaryId = null, IReadOnlyList<long>? chapterIds = null)
    {
        var dueIds = await InProgressQuery(userId, dictionaryId, chapterIds)
            .Where(p => p.DueAt <= nowUtc)
            .OrderBy(p => p.DueAt)
            .Take(size)
            .Select(p => p.WordPairId)
            .ToListAsync();

        if (dueIds.Count == 0)
        {
            return [];
        }

        var words = await _dbContext.Words
            .Where(w => dueIds.Contains(w.Id))
            .ToListAsync();

        // The DueAt order is lost when the words are loaded — restore it.
        return dueIds
            .Select(id => words.First(w => w.Id == id))
            .ToList();
    }

    /// <summary>
    /// Random translated words to serve as wrong options. The dictionary's own words first —
    /// natural distractors for a book — topped up from the rest of the vocabulary when the
    /// dictionary is smaller than the pool, so a three-word personal dictionary (or a top-100
    /// list) still fills every question. Other users' personal words never appear.
    /// </summary>
    public async Task<IReadOnlyList<WordPair>> GetDistractorPoolAsync(long userId, long? dictionaryId, int size, Random rng)
    {
        // Only words the user could meet on their own screens. The top-up branch below used to
        // reach the whole table, so any translated shared row — including one cached by someone
        // else's reader lookup — could appear as an option in this user's exercise.
        // Deliberately not the admin superset: an admin's exercise reads better from the books
        // they can actually see, and a distractor is not a curation tool.
        var visible = _dbContext.Words
            .Where(w => w.Translation != "")
            .Where(w => w.OwnerId == null || w.OwnerId == userId)
            .Where(w => w.Dictionaries.Any(d => d.PublicationStatus == PublicationStatus.Published || d.OwnerId == userId));

        var picked = new List<long>(size);

        if (dictionaryId.HasValue)
        {
            var inDictionary = await visible
                .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId.Value))
                .Select(w => w.Id)
                .ToListAsync();

            picked.AddRange(PickRandom(inDictionary, size, rng));
        }

        if (picked.Count < size)
        {
            var rest = await visible
                .Where(w => !picked.Contains(w.Id))
                .Select(w => w.Id)
                .ToListAsync();

            picked.AddRange(PickRandom(rest, size - picked.Count, rng));
        }

        if (picked.Count == 0)
        {
            return [];
        }

        var words = await _dbContext.Words
            .Where(w => picked.Contains(w.Id))
            .ToListAsync();

        // Loading by a set of ids loses the order — restore the shuffled one.
        return picked.Select(id => words.First(w => w.Id == id)).ToList();
    }

    public Task<int> CountLearnableAsync(long userId, long dictionaryId, IReadOnlyList<long>? chapterIds = null) =>
        LearnableQuery(userId, dictionaryId, chapterIds).CountAsync();

    public Task<int> CountDueAsync(
        long userId, DateTime nowUtc, long? dictionaryId = null, IReadOnlyList<long>? chapterIds = null) =>
        InProgressQuery(userId, dictionaryId, chapterIds).CountAsync(p => p.DueAt <= nowUtc);

    /// <summary>
    /// All chapters of a dictionary in one grouped query rather than a COUNT per chapter. A
    /// chapter with nothing in progress has no entry; a word in two chapters counts in each.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, ReviewAvailability>> GetReviewAvailabilityByChapterAsync(
        long userId, long dictionaryId, DateTime nowUtc)
    {
        var rows = await InProgressQuery(userId, dictionaryId, chapterIds: null)
            .Join(
                _dbContext.ChapterWords.Where(cw => cw.Chapter.DictionaryId == dictionaryId),
                p => p.WordPairId,
                cw => cw.WordPairId,
                (p, cw) => new { cw.ChapterId, p.DueAt })
            .GroupBy(x => x.ChapterId)
            .Select(g => new
            {
                ChapterId = g.Key,
                DueCount = g.Count(x => x.DueAt <= nowUtc),
                NextDueAt = g.Where(x => x.DueAt > nowUtc).Min(x => x.DueAt),
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.ChapterId, r => new ReviewAvailability(r.DueCount, r.NextDueAt));
    }

    /// <summary>
    /// Unlearned progress rows with a due date, optionally narrowed to a dictionary and to
    /// chapters. The chapter filter is the same one LearnableQuery uses: an empty list means
    /// the whole book.
    /// </summary>
    private IQueryable<WordProgress> InProgressQuery(long userId, long? dictionaryId, IReadOnlyList<long>? chapterIds)
    {
        var query = _dbContext.WordProgresses
            .Where(p => p.UserId == userId && !p.IsLearned && p.DueAt != null);

        if (dictionaryId.HasValue)
        {
            var inDictionary = _dbContext.Words
                .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId.Value))
                .Select(w => w.Id);

            query = query.Where(p => inDictionary.Contains(p.WordPairId));
        }

        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            query = query.Where(p => inChapters.Contains(p.WordPairId));
        }

        return query;
    }

    private IQueryable<WordPair> LearnableQuery(long userId, long dictionaryId, IReadOnlyList<long>? chapterIds)
    {
        var query = _dbContext.Words
            .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId))
            .Where(w => w.Translation != "")
            .Where(w => _dbContext.UnknownWords.Any(u => u.UserId == userId && u.WordPairId == w.Id))
            .Where(w => !_dbContext.KnownWords.Any(k => k.UserId == userId && k.WordPairId == w.Id))
            .Where(w => !_dbContext.ExcludedWords.Any(e => e.UserId == userId && e.WordPairId == w.Id))
            .Where(w => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

        // Chapter scope as in WordSortingService.ScopedQuery: an empty list means "the whole book".
        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            query = query.Where(w => inChapters.Contains(w.Id));
        }

        return query;
    }

    /// <summary>Partial Fisher–Yates shuffle: only the first count positions are shuffled.</summary>
    private static List<long> PickRandom(List<long> source, int count, Random rng)
    {
        var take = Math.Min(count, source.Count);

        for (var i = 0; i < take; i++)
        {
            var j = rng.Next(i, source.Count);
            (source[i], source[j]) = (source[j], source[i]);
        }

        return source.GetRange(0, take);
    }
}
