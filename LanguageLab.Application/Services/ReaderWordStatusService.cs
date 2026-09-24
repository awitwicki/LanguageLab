using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum ReaderWordStatus
{
    New,
    Learning,
    Known,
}

/// <summary>Word texts, ordinal-sorted. A word in neither list is new to the learner.</summary>
public sealed record WordStatuses(IReadOnlyList<string> Learning, IReadOnlyList<string> Known);

/// <summary>
/// The learner's standing on each word text, for the reader's highlights. Per row first: a
/// Leitner progress row decides (learned → known, else learning) because graduation leaves the
/// "don't know" row in place; without one, "don't know" → learning, "know" or "exclude" → known
/// (an excluded word is one the learner asked never to see again). Then per text: a shared and a
/// personal row of the same word that disagree make it learning.
/// </summary>
public class ReaderWordStatusService
{
    private readonly ApplicationDbContext _dbContext;

    public ReaderWordStatusService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WordStatuses> GetAsync(long userId)
    {
        var byWord = await LoadAsync(userId, word: null);

        return new WordStatuses(
            Pick(byWord, ReaderWordStatus.Learning),
            Pick(byWord, ReaderWordStatus.Known));
    }

    public async Task<ReaderWordStatus> GetAsync(long userId, string word)
    {
        var byWord = await LoadAsync(userId, word);

        return byWord.GetValueOrDefault(word, ReaderWordStatus.New);
    }

    private async Task<Dictionary<string, ReaderWordStatus>> LoadAsync(long userId, string? word)
    {
        var progress = _dbContext.WordProgresses.Where(p => p.UserId == userId);
        var unknown = _dbContext.UnknownWords.Where(u => u.UserId == userId);
        var known = _dbContext.KnownWords.Where(k => k.UserId == userId);
        var excluded = _dbContext.ExcludedWords.Where(e => e.UserId == userId);

        if (word != null)
        {
            progress = progress.Where(p => p.WordPair.Word == word);
            unknown = unknown.Where(u => u.WordPair.Word == word);
            known = known.Where(k => k.WordPair.Word == word);
            excluded = excluded.Where(e => e.WordPair.Word == word);
        }

        var perRow = new Dictionary<long, (string Word, ReaderWordStatus Status)>();

        // Lowest precedence first: each later loop overwrites the rows it knows about.
        foreach (var row in await excluded.Select(e => new { e.WordPairId, e.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordStatus.Known);
        }

        foreach (var row in await known.Select(k => new { k.WordPairId, k.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordStatus.Known);
        }

        foreach (var row in await unknown.Select(u => new { u.WordPairId, u.WordPair.Word }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, ReaderWordStatus.Learning);
        }

        foreach (var row in await progress.Select(p => new { p.WordPairId, p.WordPair.Word, p.IsLearned }).ToListAsync())
        {
            perRow[row.WordPairId] = (row.Word, row.IsLearned ? ReaderWordStatus.Known : ReaderWordStatus.Learning);
        }

        var byWord = new Dictionary<string, ReaderWordStatus>(StringComparer.Ordinal);

        foreach (var (text, status) in perRow.Values)
        {
            byWord[text] = status == ReaderWordStatus.Learning
                || (byWord.TryGetValue(text, out var earlier) && earlier == ReaderWordStatus.Learning)
                    ? ReaderWordStatus.Learning
                    : status;
        }

        return byWord;
    }

    private static IReadOnlyList<string> Pick(Dictionary<string, ReaderWordStatus> byWord, ReaderWordStatus status) =>
        byWord.Where(p => p.Value == status).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();
}
