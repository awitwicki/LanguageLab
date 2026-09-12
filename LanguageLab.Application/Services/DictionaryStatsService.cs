using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record TopWord(long WordPairId, string Word, int Frequency);

/// <summary>
/// Цифри по словнику для екрана статистики. Окремо від WordSortingService:
/// тут немає юзерських полиць, крім фільтра виключених слів нижче.
/// </summary>
public class DictionaryStatsService
{
    public const int DefaultTopWords = 10;
    public const int MaxTopWords = 50;

    private readonly ApplicationDbContext _dbContext;

    public DictionaryStatsService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Найчастіші слова словника за книжковою частотою, без слів, які цей юзер
    /// вже виключив (вони підмінюються наступними за частотою). При рівній
    /// частоті — за алфавітом, щоб порядок був стабільним між запитами.
    /// </summary>
    public async Task<IReadOnlyList<TopWord>> GetTopWordsAsync(long dictionaryId, long userId, int take = DefaultTopWords)
    {
        take = Math.Clamp(take, 1, MaxTopWords);

        return await _dbContext.DictionaryWords
            .Where(dw => dw.DictionaryId == dictionaryId)
            .Where(dw => !_dbContext.ExcludedWords.Any(e => e.UserId == userId && e.WordPairId == dw.WordPairId))
            .OrderByDescending(dw => dw.Frequency)
            .ThenBy(dw => dw.WordPair.Word)
            .Take(take)
            .Select(dw => new TopWord(dw.WordPairId, dw.WordPair.Word, dw.Frequency))
            .ToListAsync();
    }
}
