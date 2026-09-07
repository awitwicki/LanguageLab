using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>Три полиці, на які юзер розкладає слово. Взаємовиключні.</summary>
public enum SortStatus
{
    Known = 0,
    Unknown = 1,
    Excluded = 2
}

public sealed record QueueWord(long WordPairId, string Word, int Frequency);

public sealed record SortingQueue(IReadOnlyList<QueueWord> Words, int Total, int Sorted, int Remaining);

public sealed record ChapterProgress(long ChapterId, int Total, int Sorted);

public sealed record UndoResult(long WordPairId, string Word, SortStatus PreviousStatus);

public sealed record RecentWord(long WordPairId, string Word);

public sealed record RecentWords(IReadOnlyList<RecentWord> Known, IReadOnlyList<RecentWord> Unknown);

/// <summary>
/// Черга слів на сортування та операції над трьома полицями.
/// «Посортоване» = лежить на будь-якій із них.
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
            // ThenBy по id, щоб порядок був детермінований: інакше дозаливка буфера
            // могла б віддати те саме слово двічі або пропустити інше.
            .OrderByDescending(dw => dw.Frequency)
            .ThenBy(dw => dw.WordPairId)
            .Take(take)
            .Select(dw => new QueueWord(dw.WordPairId, dw.WordPair.Word, dw.Frequency))
            .ToListAsync();

        return new SortingQueue(words, total, total - remaining, remaining);
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
    /// Кладе слово на одну полицю й прибирає з двох інших. Ексклюзивність тут
    /// не формальність: унікальні індекси стоять на кожній полиці окремо, тож
    /// без цього слово могло б опинитись і в KnownWords, і в UnknownWords —
    /// і тихо випасти з навчання назавжди.
    /// </summary>
    public async Task MarkAsync(long userId, long wordPairId, SortStatus status, DateTime nowUtc)
    {
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
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Невідома полиця.")
        };

        // Повторна та сама позначка — no-op: інакше слово підстрибнуло б угору
        // колонки «останні 10» без жодної причини.
        if (alreadyThere)
        {
            return;
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
    }

    /// <summary>
    /// Знімає найсвіжішу позначку юзера, з якої б вона не була полиці.
    /// Серверний, а не клієнтський, тому переживає перезавантаження сторінки.
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
            .Select(w => w.Word)
            .SingleAsync();

        return new UndoResult(newest.WordPairId, word, newest.Status);
    }

    /// <summary>
    /// Наповнення лівої та правої колонок. Виключені сюди не входять: це не
    /// «результат сортування», а прибирання сміття.
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
    /// Слова словника, за потреби звужені до обраних глав. Частота завжди
    /// книжкова — вона й у главі означає «наскільки це слово взагалі поширене тут».
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
