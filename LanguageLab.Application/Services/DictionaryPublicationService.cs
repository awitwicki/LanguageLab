using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public enum PublicationActionResult
{
    Ok,

    /// <summary>No such dictionary for this caller — someone else's, or a personal list.</summary>
    NotFound,

    /// <summary>The dictionary is real, but not in a state this action applies to.</summary>
    WrongState,
}

/// <summary>One row of the moderation queue. TopWords is enough to judge an import by eye.</summary>
public sealed record PendingDictionary(
    long Id,
    string Name,
    long? OwnerId,
    string OwnerName,
    int WordsCount,
    PublicationStatus Status,
    IReadOnlyList<string> TopWords);

public sealed record PendingDictionaryPage(IReadOnlyList<PendingDictionary> Items, int Total, int Page, int PageSize);

/// <summary>
/// The road from a private import to a dictionary everybody sees. The owner may only offer and
/// withdraw; the decision is an admin's, and Rejected is a dead end the owner cannot re-enter —
/// otherwise a refusal is just a prompt to ask again.
/// </summary>
public class DictionaryPublicationService
{
    private readonly ApplicationDbContext _dbContext;

    public DictionaryPublicationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Words shown per row: enough to recognise a book, few enough to scan.</summary>
    public const int TopWords = 20;

    /// <summary>Oldest first — the queue reads as a queue. Paging is clamped, not refused.</summary>
    public async Task<PendingDictionaryPage> ListAsync(PublicationStatus status, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var query = _dbContext.Dictionaries.Where(d => !d.IsPersonal && d.PublicationStatus == status);
        var total = await query.CountAsync();

        var rows = await query
            .OrderBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.OwnerId,
                d.WordsCount,
                d.PublicationStatus,
                d.Owner,
                TopWords = _dbContext.DictionaryWords
                    .Where(dw => dw.DictionaryId == d.Id)
                    .OrderByDescending(dw => dw.Frequency)
                    .ThenBy(dw => dw.WordPair.Word)
                    .Take(TopWords)
                    .Select(dw => dw.WordPair.Word)
                    .ToList(),
            })
            .ToListAsync();

        var items = rows
            .Select(r => new PendingDictionary(
                r.Id,
                r.Name,
                r.OwnerId,
                r.Owner?.DisplayName ?? "System",
                r.WordsCount,
                r.PublicationStatus,
                r.TopWords))
            .ToList();

        return new PendingDictionaryPage(items, total, page, pageSize);
    }

    public Task<PublicationActionResult> RequestAsync(long userId, long dictionaryId) =>
        MoveAsync(userId, dictionaryId, PublicationStatus.Private, PublicationStatus.Pending);

    public Task<PublicationActionResult> WithdrawAsync(long userId, long dictionaryId) =>
        MoveAsync(userId, dictionaryId, PublicationStatus.Pending, PublicationStatus.Private);

    /// <summary>The admin's decision. Any state to any state — that is what curation is.</summary>
    public async Task<PublicationActionResult> SetStatusAsync(long dictionaryId, PublicationStatus status)
    {
        var dictionary = await Find(dictionaryId).FirstOrDefaultAsync();

        if (dictionary == null)
        {
            return PublicationActionResult.NotFound;
        }

        dictionary.PublicationStatus = status;
        await _dbContext.SaveChangesAsync();

        return PublicationActionResult.Ok;
    }

    private async Task<PublicationActionResult> MoveAsync(
        long userId, long dictionaryId, PublicationStatus from, PublicationStatus to)
    {
        var dictionary = await Find(dictionaryId).FirstOrDefaultAsync(d => d.OwnerId == userId);

        if (dictionary == null)
        {
            return PublicationActionResult.NotFound;
        }

        if (dictionary.PublicationStatus != from)
        {
            return PublicationActionResult.WrongState;
        }

        dictionary.PublicationStatus = to;
        await _dbContext.SaveChangesAsync();

        return PublicationActionResult.Ok;
    }

    private IQueryable<Domain.Entities.Dictionary> Find(long dictionaryId) =>
        _dbContext.Dictionaries.Where(d => d.Id == dictionaryId && !d.IsPersonal);
}
