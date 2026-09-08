using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>Слово-кандидат у батч із частотою в тому скоупі, який тренуємо.</summary>
public sealed record Candidate(long WordPairId, string Word, string Translation, int Frequency);

/// <summary>
/// Вирішує, які слова показувати. Тут живе правило «не вчити те, що вже знаю»:
/// слово потрапляє в новий батч, тільки якщо воно є в цьому словнику, має переклад,
/// позначене юзером як «хочу вчити», не позначене як відоме, не виключене юзером і ще жодного разу не тренувалося.
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
    /// Найчастіші learnable-слова скоупу за частотою цього скоупу: по главі — сума ChapterWord.Count
    /// обраних глав, по книжці — DictionaryWord.Frequency. Порядок детермінований (частота, слово, id),
    /// бо превью на екрані старту має збігатися з батчем.
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

    /// <summary>Перші size кандидатів як WordPair, у тому ж порядку. Порядок питань у квізі тасує QuestionQueueBuilder.</summary>
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

        // Завантаження за набором id не зберігає порядок — відновлюємо порядок кандидатів.
        return ids.Select(id => words.First(w => w.Id == id)).ToList();
    }

    /// <summary>
    /// Підмножина ids, що досі learnable у скоупі, у порядку ids, без дублів. Чужі, зниклі
    /// (викреслені в іншій вкладці, уже треновані) id мовчки відкидаються — це гонка, не помилка клієнта.
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

    public async Task<IReadOnlyList<WordPair>> GetDueWordsAsync(long userId, DateTime nowUtc, int size)
    {
        var dueIds = await _dbContext.WordProgresses
            .Where(p => p.UserId == userId && !p.IsLearned && p.DueAt != null && p.DueAt <= nowUtc)
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

        // Порядок за DueAt губиться при завантаженні слів — відновлюємо його.
        return dueIds
            .Select(id => words.First(w => w.Id == id))
            .ToList();
    }

    public async Task<IReadOnlyList<WordPair>> GetDistractorPoolAsync(long? dictionaryId, int size, Random rng)
    {
        var query = _dbContext.Words.Where(w => w.Translation != "");

        if (dictionaryId.HasValue)
        {
            query = query.Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId.Value));
        }

        var ids = await query.Select(w => w.Id).ToListAsync();

        if (ids.Count == 0)
        {
            return [];
        }

        var picked = PickRandom(ids, size, rng);

        var words = await _dbContext.Words
            .Where(w => picked.Contains(w.Id))
            .ToListAsync();

        // Завантаження за набором id не зберігає порядок — відновлюємо перемішаний.
        return picked.Select(id => words.First(w => w.Id == id)).ToList();
    }

    public Task<int> CountLearnableAsync(long userId, long dictionaryId, IReadOnlyList<long>? chapterIds = null) =>
        LearnableQuery(userId, dictionaryId, chapterIds).CountAsync();

    public Task<int> CountDueAsync(long userId, DateTime nowUtc) =>
        _dbContext.WordProgresses
            .CountAsync(p => p.UserId == userId && !p.IsLearned && p.DueAt != null && p.DueAt <= nowUtc);

    private IQueryable<WordPair> LearnableQuery(long userId, long dictionaryId, IReadOnlyList<long>? chapterIds)
    {
        var query = _dbContext.Words
            .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId))
            .Where(w => w.Translation != "")
            .Where(w => _dbContext.UnknownWords.Any(u => u.UserId == userId && u.WordPairId == w.Id))
            .Where(w => !_dbContext.KnownWords.Any(k => k.UserId == userId && k.WordPairId == w.Id))
            .Where(w => !_dbContext.ExcludedWords.Any(e => e.UserId == userId && e.WordPairId == w.Id))
            .Where(w => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

        // Скоуп по главах — як у WordSortingService.ScopedQuery: порожній список означає «вся книжка».
        if (chapterIds is { Count: > 0 })
        {
            var inChapters = _dbContext.ChapterWords
                .Where(cw => chapterIds.Contains(cw.ChapterId))
                .Select(cw => cw.WordPairId);

            query = query.Where(w => inChapters.Contains(w.Id));
        }

        return query;
    }

    /// <summary>Часткове перемішування Фішера—Йетса: тасуємо лише перші count позицій.</summary>
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
