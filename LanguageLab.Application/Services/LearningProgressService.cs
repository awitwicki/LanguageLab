using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// Розклад слів скоупу по боксах Leitner. Boxes: індекс 0 = бокс 1, лише не вивчені слова;
/// вивчені (IsLearned) — окремо в Learned. Total іде в JSON разом з усім — відсоток рахує клієнт.
/// </summary>
public sealed record LearningProgress(int NotStarted, IReadOnlyList<int> Boxes, int Learned)
{
    public int Total => NotStarted + Boxes.Sum() + Learned;

    public static LearningProgress Empty => new(0, new int[LeitnerScheduler.MaxBox], 0);
}

/// <summary>
/// «Слова, які юзер вирішив вивчити» у скоупі: у словнику (і в котрійсь із заданих глав),
/// з перекладом, не виключені, і або на полиці «не знаю», або вже з рядком WordProgress.
/// NotStarted за побудовою дорівнює WordSelectionService.CountLearnableAsync того ж скоупу —
/// той самий предикат мінус «уже має прогрес»; тест NotStarted_equals_CountLearnable пінить це.
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

        // Скоуп по главах — як у WordSortingService.ScopedQuery: порожній список означає «вся книжка».
        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            tracked = tracked.Where(w => inChapters.Contains(w.Id));
        }

        var notStarted = await tracked
            .CountAsync(w => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

        // Два прості запити замість left join + group by: рядок прогресу на слово щонайбільше один,
        // тож групувати можна одразу WordProgresses, а «не почато» — окремий COUNT.
        var rows = await _dbContext.WordProgresses
            .Where(p => p.UserId == userId)
            .Where(p => tracked.Any(w => w.Id == p.WordPairId))
            .GroupBy(p => new { p.Box, p.IsLearned })
            .Select(g => new { g.Key.Box, g.Key.IsLearned, Count = g.Count() })
            .ToListAsync();

        return Fold(notStarted, rows.Select(r => new BoxRow(r.Box, r.IsLearned, r.Count)));
    }

    /// <summary>Усі глави словника: два запити на словник, а не на главу. Слово з двох глав рахується в кожній.</summary>
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
