using LanguageLab.Domain.Entities;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>A starred chapter as the home screen lists it: the row plus which book it belongs to.</summary>
public sealed record StarredChapterView(long DictionaryId, string DictionaryName, ChapterView Chapter);

/// <summary>
/// A user's starred chapters. Starring checks the book is visible to the caller — a star is
/// a bookmark into something they can open. Unstarring does not: a star on a book that has
/// since gone private is invisible in the list, but the user may still drop it.
/// </summary>
public class StarredChapterService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DictionaryAccessService _access;
    private readonly ChapterStatsService _chapterStats;

    public StarredChapterService(
        ApplicationDbContext dbContext, DictionaryAccessService access, ChapterStatsService chapterStats)
    {
        _dbContext = dbContext;
        _access = access;
        _chapterStats = chapterStats;
    }

    /// <summary>False when the chapter does not exist or its book is not visible to the caller. Idempotent.</summary>
    public async Task<bool> StarAsync(long userId, UserRole role, long chapterId, DateTime nowUtc)
    {
        var chapter = await _dbContext.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => new { c.DictionaryId })
            .FirstOrDefaultAsync();

        if (chapter == null || !await _access.IsVisibleAsync(chapter.DictionaryId, userId, role))
        {
            return false;
        }

        var alreadyStarred = await _dbContext.StarredChapters
            .AnyAsync(s => s.UserId == userId && s.ChapterId == chapterId);

        if (alreadyStarred)
        {
            return true;
        }

        var star = new StarredChapter { UserId = userId, ChapterId = chapterId, CreatedAt = nowUtc };
        _dbContext.StarredChapters.Add(star);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two tabs starring at once: the unique index rejects the second insert, and the
            // outcome the second caller wanted is already there. Detach so the stale Added
            // entity doesn't get retried on the next SaveChangesAsync in this scope.
            _dbContext.Entry(star).State = EntityState.Detached;
        }

        return true;
    }

    /// <summary>False when nothing was starred — the caller answers 404 to a stale click.</summary>
    public async Task<bool> UnstarAsync(long userId, long chapterId)
    {
        var star = await _dbContext.StarredChapters
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ChapterId == chapterId);

        if (star == null)
        {
            return false;
        }

        _dbContext.StarredChapters.Remove(star);
        await _dbContext.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Every star whose book the caller can still see, ordered by book name and then chapter
    /// order. One round of per-book queries per starred book, per-chapter COUNTs only for
    /// the starred chapters.
    /// </summary>
    public async Task<IReadOnlyList<StarredChapterView>> GetStarredAsync(long userId, UserRole role, DateTime nowUtc)
    {
        var stars = await _dbContext.StarredChapters
            .Where(s => s.UserId == userId)
            .Select(s => new { s.ChapterId, s.Chapter.DictionaryId })
            .ToListAsync();

        if (stars.Count == 0)
        {
            return [];
        }

        var dictionaryIds = stars.Select(s => s.DictionaryId).Distinct().ToList();

        var books = await _access.Visible(userId, role)
            .Where(d => dictionaryIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .OrderBy(d => d.Name)
            .ToListAsync();

        var result = new List<StarredChapterView>(stars.Count);

        foreach (var book in books)
        {
            var chapterIds = stars.Where(s => s.DictionaryId == book.Id).Select(s => s.ChapterId).ToList();
            var views = await _chapterStats.GetChapterViewsAsync(userId, book.Id, nowUtc, chapterIds);

            result.AddRange(views.Select(v => new StarredChapterView(book.Id, book.Name, v)));
        }

        return result;
    }
}
