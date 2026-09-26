using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// The chapter half of a scope. Only what a label needs — the SPA turns an empty title into
/// "Chapter N" from the order. Deliberately not a <see cref="ChapterView"/>: these rows are a
/// way back into a scope, not a report on its standing, and a ChapterView costs several
/// COUNTs per chapter to build.
/// </summary>
public sealed record ScopeChapter(long Id, int Order, string Title);

/// <summary>
/// One finished session, as the home screen's "Repeat" offers it again. DictionaryId null —
/// a review across every book, which has no scope to name.
/// </summary>
public sealed record RecentExercise(
    long TrainingId,
    TrainingMode Mode,
    DateTime FinishedAt,
    long? DictionaryId,
    string? DictionaryName,
    ScopeChapter? Chapter,
    int Correct,
    int Total);

/// <summary>
/// One scope the user sorted in and has not finished. Chapter null — the whole book.
/// Total and Sorted are that scope's, so the row can show how far it got.
/// </summary>
public sealed record RecentSorting(
    long DictionaryId,
    string DictionaryName,
    ScopeChapter? Chapter,
    DateTime LastSortedAt,
    int Total,
    int Sorted);

public sealed record RecentActivity(
    IReadOnlyList<RecentExercise> Exercises,
    IReadOnlyList<RecentSorting> Sorting);

/// <summary>
/// What the home screen offers to pick up again: the last exercises, and the scopes that were
/// being sorted and are not done. Both lists are shortcuts, so both drop anything the user can
/// no longer open — a book that went private is no way back.
/// </summary>
public class RecentActivityService
{
    /// <summary>Rows per list. Three is a shortcut; a fourth is a screen of history nobody asked for.</summary>
    public const int MaxRows = 3;

    /// <summary>
    /// How many rows each list reads before filtering down to <see cref="MaxRows"/>. Sessions
    /// collapse per scope and visits drop once their scope is fully sorted, so both lists need
    /// slack — but bounded slack: a user who really has only worked in one scope lately sees
    /// one row, which is the truth.
    /// </summary>
    private const int ScanLimit = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly DictionaryAccessService _access;
    private readonly WordSortingService _sorting;

    public RecentActivityService(
        ApplicationDbContext dbContext, DictionaryAccessService access, WordSortingService sorting)
    {
        _dbContext = dbContext;
        _access = access;
        _sorting = sorting;
    }

    public async Task<RecentActivity> GetAsync(long userId, UserRole role) =>
        new(await GetExercisesAsync(userId, role), await GetSortingAsync(userId, role));

    /// <summary>
    /// The newest finished session per scope and mode. Unfinished sessions are left out:
    /// picking one up where it stopped is its own thing, and listing it here as history would
    /// quietly throw away the half that is unanswered.
    /// </summary>
    private async Task<IReadOnlyList<RecentExercise>> GetExercisesAsync(long userId, UserRole role)
    {
        var finished = await _dbContext.Trainings
            .Where(t => t.UserId == userId && t.FinishedAt != null)
            .OrderByDescending(t => t.FinishedAt)
            .ThenByDescending(t => t.Id)
            .Take(ScanLimit)
            .Select(t => new
            {
                t.Id,
                t.Mode,
                FinishedAt = t.FinishedAt!.Value,
                t.DictionaryId,
                t.ChapterId
            })
            .ToListAsync();

        if (finished.Count == 0)
        {
            return [];
        }

        // Newest first already, so the first row of a scope is the one to keep.
        var seen = new HashSet<(long? DictionaryId, long? ChapterId, TrainingMode Mode)>();
        var picked = new List<(long Id, TrainingMode Mode, DateTime FinishedAt, long? DictionaryId, long? ChapterId)>();

        foreach (var session in finished)
        {
            if (seen.Add((session.DictionaryId, session.ChapterId, session.Mode)))
            {
                picked.Add((session.Id, session.Mode, session.FinishedAt, session.DictionaryId, session.ChapterId));
            }
        }

        var books = await VisibleNamesAsync(userId, role, picked.Select(p => p.DictionaryId));
        var chapters = await ChaptersAsync(picked.Select(p => p.ChapterId));
        var scores = await ScoresAsync(picked.Select(p => p.Id));

        var result = new List<RecentExercise>(MaxRows);

        foreach (var session in picked)
        {
            string? bookName = null;

            // A scoped session whose book is gone or no longer visible has nothing to reopen;
            // an all-books review (no dictionary) is always still there.
            if (session.DictionaryId is { } dictionaryId)
            {
                if (!books.TryGetValue(dictionaryId, out bookName))
                {
                    continue;
                }
            }

            var score = scores.GetValueOrDefault(session.Id);

            result.Add(new RecentExercise(
                session.Id,
                session.Mode,
                session.FinishedAt,
                session.DictionaryId,
                bookName,
                session.ChapterId is { } chapterId ? chapters.GetValueOrDefault(chapterId) : null,
                score.Correct,
                score.Total));

            if (result.Count == MaxRows)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// The scopes last sorted in that still have words left. A visit's row is upserted, so
    /// there is one per scope already — only the finished ones and the unreachable books go.
    /// </summary>
    private async Task<IReadOnlyList<RecentSorting>> GetSortingAsync(long userId, UserRole role)
    {
        var visits = await _dbContext.SortingVisits
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.LastSortedAt)
            .ThenByDescending(v => v.Id)
            .Take(ScanLimit)
            .Select(v => new { v.DictionaryId, v.ChapterId, v.LastSortedAt })
            .ToListAsync();

        if (visits.Count == 0)
        {
            return [];
        }

        var books = await VisibleNamesAsync(userId, role, visits.Select(v => (long?)v.DictionaryId));
        var chapters = await ChaptersAsync(visits.Select(v => v.ChapterId));

        var result = new List<RecentSorting>(MaxRows);

        foreach (var visit in visits)
        {
            if (!books.TryGetValue(visit.DictionaryId, out var bookName))
            {
                continue;
            }

            // A chapter deleted since cascades its visit away, so a missing one here means the
            // row is already gone; skip rather than pass the chapter off as the whole book.
            ScopeChapter? chapter = null;

            if (visit.ChapterId is { } chapterId)
            {
                chapter = chapters.GetValueOrDefault(chapterId);

                if (chapter == null)
                {
                    continue;
                }
            }

            var progress = await _sorting.CountScopeAsync(userId, visit.DictionaryId, visit.ChapterId);

            if (progress.Sorted >= progress.Total)
            {
                continue;
            }

            result.Add(new RecentSorting(
                visit.DictionaryId, bookName, chapter, visit.LastSortedAt, progress.Total, progress.Sorted));

            if (result.Count == MaxRows)
            {
                break;
            }
        }

        return result;
    }

    private async Task<Dictionary<long, string>> VisibleNamesAsync(
        long userId, UserRole role, IEnumerable<long?> dictionaryIds)
    {
        var ids = dictionaryIds.OfType<long>().Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _access.Visible(userId, role)
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name);
    }

    private async Task<Dictionary<long, ScopeChapter>> ChaptersAsync(IEnumerable<long?> chapterIds)
    {
        var ids = chapterIds.OfType<long>().Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _dbContext.Chapters
            .Where(c => ids.Contains(c.Id))
            .Select(c => new ScopeChapter(c.Id, c.Order, c.Title))
            .ToDictionaryAsync(c => c.Id);
    }

    /// <summary>
    /// Right-answer and question counts for the listed sessions, in one GROUP BY. A question
    /// removed by the "Know" button is gone from both halves, which is what the summary shows too.
    /// </summary>
    private async Task<Dictionary<long, (int Correct, int Total)>> ScoresAsync(IEnumerable<long> trainingIds)
    {
        var ids = trainingIds.Distinct().ToList();

        var rows = await _dbContext.TrainingQuestions
            .Where(q => ids.Contains(q.TrainingId))
            .GroupBy(q => q.TrainingId)
            .Select(g => new
            {
                TrainingId = g.Key,
                Correct = g.Count(q => q.IsCorrect == true),
                Total = g.Count()
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.TrainingId, r => (r.Correct, r.Total));
    }
}
