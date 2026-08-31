using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.TgBot.Services;

/// <summary>
/// Вирішує, які слова показувати. Тут живе правило «не вчити те, що вже знаю»:
/// слово потрапляє в новий батч, тільки якщо воно є в цьому словнику, має переклад,
/// позначене юзером як «хочу вчити», не позначене як відоме і ще жодного разу не тренувалося.
/// </summary>
public class WordSelectionService
{
    public const int NewBatchSize = 5;
    public const int ReviewSessionSize = 20;
    public const int DistractorPoolSize = 60;

    private readonly ApplicationDbContext _dbContext;

    public WordSelectionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WordPair>> GetNewBatchAsync(long userId, long dictionaryId, int size, Random rng)
    {
        var candidateIds = await LearnableQuery(userId, dictionaryId)
            .Select(w => w.Id)
            .ToListAsync();

        if (candidateIds.Count == 0)
        {
            return [];
        }

        // Вибір випадковий, а не за алфавітом: слова у файлі відсортовані,
        // а інтерлівінг запам'ятовується краще за послідовне зубріння однієї літери.
        var picked = PickRandom(candidateIds, size, rng);

        var words = await _dbContext.Words
            .Where(w => picked.Contains(w.Id))
            .ToListAsync();

        // Завантаження за набором id не зберігає порядок — відновлюємо перемішаний.
        return picked.Select(id => words.First(w => w.Id == id)).ToList();
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

    public Task<int> CountLearnableAsync(long userId, long dictionaryId) =>
        LearnableQuery(userId, dictionaryId).CountAsync();

    public Task<int> CountDueAsync(long userId, DateTime nowUtc) =>
        _dbContext.WordProgresses
            .CountAsync(p => p.UserId == userId && !p.IsLearned && p.DueAt != null && p.DueAt <= nowUtc);

    private IQueryable<WordPair> LearnableQuery(long userId, long dictionaryId) =>
        _dbContext.Words
            .Where(w => w.Dictionaries.Any(d => d.Id == dictionaryId))
            .Where(w => w.Translation != "")
            .Where(w => _dbContext.UnknownWords.Any(u => u.UserId == userId && u.WordPairId == w.Id))
            .Where(w => !_dbContext.KnownWords.Any(k => k.UserId == userId && k.WordPairId == w.Id))
            .Where(w => !_dbContext.WordProgresses.Any(p => p.UserId == userId && p.WordPairId == w.Id));

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
