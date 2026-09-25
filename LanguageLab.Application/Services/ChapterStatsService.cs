using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Application.Services;

/// <summary>
/// One chapter row as the SPA renders it — on the book page and, for starred chapters, on
/// the home screen. Lives here rather than in the API project because the starred list is
/// assembled in Application, from several books at once.
/// </summary>
public sealed record ChapterView(
    long Id,
    int Order,
    string Title,
    int WordsCount,
    int SortedCount,
    int LearnableCount,
    LearningProgress Learning,
    int DueCount,
    DateTime? NextDueAt,
    bool IsStarred);

/// <summary>
/// Builds <see cref="ChapterView"/> rows for a book. The bulk per-book queries (sorting
/// progress, Leitner boxes, review availability) run once per call; "to learn" is still a
/// COUNT per returned chapter, which is why the starred list asks for its chapters only.
/// Visibility is the caller's business — the dictionary has been authorised before this runs.
/// </summary>
public class ChapterStatsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly WordSortingService _sorting;
    private readonly WordSelectionService _selection;
    private readonly LearningProgressService _learningProgress;

    public ChapterStatsService(
        ApplicationDbContext dbContext,
        WordSortingService sorting,
        WordSelectionService selection,
        LearningProgressService learningProgress)
    {
        _dbContext = dbContext;
        _sorting = sorting;
        _selection = selection;
        _learningProgress = learningProgress;
    }

    /// <param name="onlyChapterIds">null — every chapter of the book; otherwise just these, still in book order.</param>
    public async Task<IReadOnlyList<ChapterView>> GetChapterViewsAsync(
        long userId, long dictionaryId, DateTime nowUtc, IReadOnlyList<long>? onlyChapterIds = null)
    {
        var query = _dbContext.Chapters.Where(c => c.DictionaryId == dictionaryId);

        if (onlyChapterIds != null)
        {
            query = query.Where(c => onlyChapterIds.Contains(c.Id));
        }

        var chapters = await query
            .OrderBy(c => c.Order)
            .Select(c => new { c.Id, c.Order, c.Title, c.WordsCount })
            .ToListAsync();

        if (chapters.Count == 0)
        {
            return [];
        }

        var progress = (await _sorting.GetChapterProgressAsync(userId, dictionaryId))
            .ToDictionary(p => p.ChapterId);
        var chapterLearning = await _learningProgress.GetByChapterAsync(userId, dictionaryId);

        // Once a chapter has no new words left, its row offers a review of what is due
        // there instead — or says when the next word comes due. One query for all chapters.
        var chapterReview = await _selection.GetReviewAvailabilityByChapterAsync(userId, dictionaryId, nowUtc);

        var starred = (await _dbContext.StarredChapters
                .Where(s => s.UserId == userId && s.Chapter.DictionaryId == dictionaryId)
                .Select(s => s.ChapterId)
                .ToListAsync())
            .ToHashSet();

        var views = new List<ChapterView>(chapters.Count);

        foreach (var c in chapters)
        {
            // "To learn" = translated, on the "don't know" shelf, never trained — exactly
            // what a new batch would take. One COUNT per chapter, like the sorting progress.
            var learnable = await _selection.CountLearnableAsync(userId, dictionaryId, [c.Id]);
            var review = chapterReview.TryGetValue(c.Id, out var r) ? r : ReviewAvailability.None;

            views.Add(new ChapterView(
                c.Id, c.Order, c.Title, c.WordsCount,
                progress.TryGetValue(c.Id, out var p) ? p.Sorted : 0,
                learnable,
                chapterLearning.TryGetValue(c.Id, out var l) ? l : LearningProgress.Empty,
                review.DueCount,
                review.NextDueAt,
                starred.Contains(c.Id)));
        }

        return views;
    }
}
