using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

public sealed record ReaderPosition(int ChapterIndex, int ParagraphIndex, int SentenceIndex, double Progress);

/// <summary>DictionaryId: a dictionary the caller can see that was imported from the same file, else null.</summary>
public sealed record ReaderBookView(
    string FileHash,
    string Title,
    string Author,
    int ChaptersCount,
    int ChapterIndex,
    int ParagraphIndex,
    int SentenceIndex,
    double Progress,
    DateTime UpdatedAt,
    long? DictionaryId);

/// <summary>
/// The reader's library on the server: what another device needs to continue a book whose file
/// only ever lives in a browser. Expects hashes already normalized by ReaderHash.
/// </summary>
public class ReaderBookService
{
    public const int MaxTitleLength = 300;

    private readonly ApplicationDbContext _dbContext;
    private readonly DictionaryAccessService _access;

    public ReaderBookService(ApplicationDbContext dbContext, DictionaryAccessService access)
    {
        _dbContext = dbContext;
        _access = access;
    }

    public async Task<IReadOnlyList<ReaderBookView>> ListAsync(long userId, UserRole role)
    {
        var books = await _dbContext.ReaderBooks
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.UpdatedAt)
            .ThenBy(b => b.Title)
            .ToListAsync();

        var dictionaries = await LinkedDictionariesAsync(userId, role, books.Select(b => b.FileHash).ToList());

        return books.Select(b => View(b, dictionaries)).ToList();
    }

    /// <summary>
    /// Idempotent: a second call refreshes the title, author and chapter count (the parser may
    /// have improved) but keeps the position. ArgumentException for an empty title or no chapters.
    /// </summary>
    public async Task<ReaderBookView> RegisterAsync(
        long userId, UserRole role, string fileHash, string title, string author, int chaptersCount, DateTime nowUtc)
    {
        title = Truncate(title.Trim());
        author = Truncate(author.Trim());

        if (title.Length == 0)
        {
            throw new ArgumentException("The book needs a title.");
        }

        if (chaptersCount < 1)
        {
            throw new ArgumentException("The book has no chapters.");
        }

        var book = await Find(userId, fileHash);

        if (book == null)
        {
            book = new ReaderBook { UserId = userId, FileHash = fileHash, CreatedAt = nowUtc, UpdatedAt = nowUtc };
            _dbContext.ReaderBooks.Add(book);
        }

        book.Title = title;
        book.Author = author;
        book.ChaptersCount = chaptersCount;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two first opens of the same file raced (two tabs): the unique index let one in.
            _dbContext.Entry(book).State = EntityState.Detached;
            book = await Find(userId, fileHash) ?? throw new InvalidOperationException("The racing registration vanished.");
        }

        var dictionaries = await LinkedDictionariesAsync(userId, role, [fileHash]);
        return View(book, dictionaries);
    }

    /// <summary>
    /// False when the caller has no such book. A position older than the stored one is ignored
    /// (true all the same): the later device wins. The client's clock is trusted for ordering
    /// between devices, but never beyond the server's now.
    /// </summary>
    public async Task<bool> SavePositionAsync(
        long userId, string fileHash, ReaderPosition position, DateTime clientUpdatedAt, DateTime nowUtc)
    {
        var book = await Find(userId, fileHash);

        if (book == null)
        {
            return false;
        }

        var at = clientUpdatedAt.ToUniversalTime();

        if (at > nowUtc)
        {
            at = nowUtc;
        }

        if (book.UpdatedAt > at)
        {
            return true;
        }

        book.ChapterIndex = Math.Max(0, position.ChapterIndex);
        book.ParagraphIndex = Math.Max(0, position.ParagraphIndex);
        book.SentenceIndex = Math.Max(0, position.SentenceIndex);
        book.Progress = double.IsFinite(position.Progress) ? Math.Clamp(position.Progress, 0, 1) : 0;
        book.UpdatedAt = at;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(long userId, string fileHash)
    {
        var book = await Find(userId, fileHash);

        if (book == null)
        {
            return false;
        }

        _dbContext.ReaderBooks.Remove(book);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    private Task<ReaderBook?> Find(long userId, string fileHash) =>
        _dbContext.ReaderBooks.FirstOrDefaultAsync(b => b.UserId == userId && b.FileHash == fileHash);

    /// <summary>File hash → the lowest id among the visible dictionaries imported from it.</summary>
    private async Task<Dictionary<string, long>> LinkedDictionariesAsync(long userId, UserRole role, List<string> hashes)
    {
        var rows = await _access.Visible(userId, role)
            .Where(d => d.FileHash != null && hashes.Contains(d.FileHash))
            .Select(d => new { d.Id, d.FileHash })
            .ToListAsync();

        return rows
            .GroupBy(d => d.FileHash!)
            .ToDictionary(g => g.Key, g => g.Min(d => d.Id));
    }

    private static ReaderBookView View(ReaderBook book, Dictionary<string, long> dictionaries) =>
        new(
            book.FileHash,
            book.Title,
            book.Author,
            book.ChaptersCount,
            book.ChapterIndex,
            book.ParagraphIndex,
            book.SentenceIndex,
            book.Progress,
            book.UpdatedAt,
            dictionaries.TryGetValue(book.FileHash, out var id) ? id : null);

    private static string Truncate(string value) =>
        value.Length <= MaxTitleLength ? value : value[..MaxTitleLength];
}
