using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record TopWord(long WordPairId, string Word, int Frequency);

/// <summary>
/// The dictionary's numbers for the stats screen. Kept apart from WordSortingService:
/// no user shelves here, except the excluded-words filter below.
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
    /// The dictionary's most frequent words by book frequency, minus the words this
    /// user has already excluded (the next ones by frequency take their place). Ties
    /// are broken alphabetically so the order is stable between requests.
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
