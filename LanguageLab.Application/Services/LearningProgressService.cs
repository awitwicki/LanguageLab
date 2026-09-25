using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// How a scope's words are spread across the Leitner boxes. Boxes: index 0 = box 1, unlearned
/// words only; learned ones (IsLearned) sit apart in Learned. Total ships in the JSON with the
/// rest — the client computes the percentage.
/// </summary>
public sealed record LearningProgress(int NotStarted, IReadOnlyList<int> Boxes, int Learned)
{
    public int Total => NotStarted + Boxes.Sum() + Learned;

    public static LearningProgress Empty => new(0, new int[LeitnerScheduler.MaxBox], 0);
}

/// <summary>
/// "Words the user decided to learn" within a scope: in the dictionary (and in one of the given
/// chapters), translated, not excluded, and either on the "don't know" shelf or already holding
/// a WordProgress row. By construction NotStarted equals WordSelectionService.CountLearnableAsync
/// for the same scope — the same predicate minus "already has progress"; the
/// NotStarted_equals_CountLearnable test pins that.
/// </summary>
public class LearningProgressService
{
    private readonly ApplicationDbContext _dbContext;

    public LearningProgressService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LearningProgress> GetAsync(
        long userId, long dictionaryId, IReadOnlyList<long>? chapterIds = null)
    {
        var tracked = TrackedWords(userId, dictionaryId);

        // Chapter scope as in WordSortingService.ScopedQuery: an empty list means "the whole book".
        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            tracked = tracked.Where(w => inChapters.Contains(w.Id));
        }

        var notStarted = await tracked
            .CountAsync(w => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

        // Two plain queries instead of a left join + group by: there is at most one progress row
        // per word, so WordProgresses can be grouped directly and "not started" is a separate COUNT.
        var rows = await _dbContext.WordProgresses
            .Where(p => p.UserId == userId)
            .Where(p => tracked.Any(w => w.Id == p.WordPairId))
            .GroupBy(p => new { p.Box, p.IsLearned })
            .Select(g => new { g.Key.Box, g.Key.IsLearned, Count = g.Count() })
            .ToListAsync();

        return Fold(notStarted, rows.Select(r => new BoxRow(r.Box, r.IsLearned, r.Count)));
    }

    /// <summary>Every chapter of a dictionary: two queries per dictionary, not per chapter. A word in two chapters counts in each.</summary>
    public async Task<IReadOnlyDictionary<long, LearningProgress>> GetByChapterAsync(long userId, long dictionaryId)
    {
        var tracked = TrackedWords(userId, dictionaryId);

        var chapterWords = _dbContext.ChapterWords
            .Where(cw => cw.Chapter.DictionaryId == dictionaryId)
            .Where(cw => tracked.Any(w => w.Id == cw.WordPairId));

        var notStarted = await chapterWords
            .Where(cw => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == cw.WordPairId))
            .GroupBy(cw => cw.ChapterId)
            .Select(g => new { ChapterId = g.Key, Count = g.Count() })
            .ToListAsync();

        var rows = await chapterWords
            .Join(
                _dbContext.WordProgresses.Where(p => p.UserId == userId),
                cw => cw.WordPairId,
                p => p.WordPairId,
                (cw, p) => new { cw.ChapterId, p.Box, p.IsLearned })
            .GroupBy(x => new { x.ChapterId, x.Box, x.IsLearned })
            .Select(g => new { g.Key.ChapterId, g.Key.Box, g.Key.IsLearned, Count = g.Count() })
            .ToListAsync();

        var result = new Dictionary<long, LearningProgress>();
        var chapterIds = notStarted.Select(n => n.ChapterId).Concat(rows.Select(r => r.ChapterId)).Distinct();

        foreach (var chapterId in chapterIds)
        {
            var started = notStarted.FirstOrDefault(n => n.ChapterId == chapterId)?.Count ?? 0;
            var chapterRows = rows
                .Where(r => r.ChapterId == chapterId)
                .Select(r => new BoxRow(r.Box, r.IsLearned, r.Count));

            result[chapterId] = Fold(started, chapterRows);
        }

        return result;
    }

    private IQueryable<WordPair> TrackedWords(long userId, long dictionaryId) =>
        _dbContext.Words
            .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId))
            .Where(w => w.Translation != "")
            .Where(w => !_dbContext.ExcludedWords.Any(e => e.UserId == userId && e.WordPairId == w.Id))
            .Where(w => _dbContext.UnknownWords.Any(u => u.UserId == userId && u.WordPairId == w.Id)
                     || _dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

    private sealed record BoxRow(int Box, bool IsLearned, int Count);

    private static LearningProgress Fold(int notStarted, IEnumerable<BoxRow> rows)
    {
        var boxes = new int[LeitnerScheduler.MaxBox];
        var learned = 0;

        foreach (var row in rows)
        {
            if (row.IsLearned)
            {
                learned += row.Count;
                continue;
            }

            boxes[Math.Clamp(row.Box, LeitnerScheduler.MinBox, LeitnerScheduler.MaxBox) - 1] += row.Count;
        }

        return new LearningProgress(notStarted, boxes, learned);
    }
}
