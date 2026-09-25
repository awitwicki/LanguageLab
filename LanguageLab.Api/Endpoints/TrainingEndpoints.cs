using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

public sealed record NewBatchRequest(
    long DictionaryId, IReadOnlyList<long>? ChapterIds, int BatchSize, IReadOnlyList<long>? WordPairIds);

public sealed record BatchPreview(LearningProgress Learning, int LearnableCount, IReadOnlyList<Candidate> Candidates);

/// <summary>Optional: without a body the review spans every book; with one it is that book's, or those chapters'.</summary>
public sealed record ReviewRequest(long DictionaryId, IReadOnlyList<long>? ChapterIds);

public sealed record BatchWord(long WordPairId, string Word, string Translation);

public sealed record TrainingStarted(long TrainingId, TrainingMode Mode, IReadOnlyList<BatchWord> Words, int TotalQuestions);

public sealed record QuestionOption(long WordPairId, string Label);

public sealed record QuestionDto(
    long Id, long WordPairId, QuestionDirection Direction, string Prompt, IReadOnlyList<QuestionOption> Options);

public sealed record NextQuestion(QuestionDto? Question, int Answered, int Total);

public sealed record AnswerRequest(long QuestionId, long PickedWordPairId);

public sealed record AnswerResult(bool IsCorrect, long CorrectWordPairId, string Word, string Translation);

public sealed record KnownRequest(long QuestionId);

public sealed record KnownResult(string Word);

/// <summary>
/// A thin layer over TrainingSessionService: session ownership checks, DTO mapping and
/// response codes — no learning logic. The question queue lives in the DB, so the client
/// can reload mid-quiz without losing the session state.
/// </summary>
public static class TrainingEndpoints
{
    public static void MapTrainingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/training").RequireAuthorization();

        // The start screen's preview: the scope's scale + the top candidates by frequency. What is shown
        // here the client later passes to new-batch as explicit ids. A missing dictionary yields an empty preview, just as new-batch yields 204.
        group.MapGet("/preview", async (
            long dictionaryId,
            string? chapterIds,
            int? take,
            WordSelectionService selection,
            LearningProgressService learningProgress,
            DictionaryAccessService access,
            ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            if (!await access.IsVisibleAsync(dictionaryId, userId, role))
            {
                return Results.NotFound();
            }

            var chapters = QueryParsing.ParseChapterIds(chapterIds);

            var learning = await learningProgress.GetAsync(userId, dictionaryId, chapters);
            var learnable = await selection.CountLearnableAsync(userId, dictionaryId, chapters);
            var candidates = await selection.GetCandidatesAsync(
                userId, dictionaryId, chapters, take ?? WordSelectionService.MaxCandidates);

            return Results.Ok(new BatchPreview(learning, learnable, candidates));
        });

        // The user's Leitner standing across every book: per-box counts of words in progress,
        // learned, and what is due right now. Fed to the home screen.
        group.MapGet("/stats", async (TrainingSessionService sessions, ICurrentUser currentUser) =>
            Results.Ok(await sessions.GetStatsAsync(await currentUser.GetIdAsync(), DateTime.UtcNow)));

        group.MapPost("/new-batch", async (
            NewBatchRequest request,
            TrainingSessionService sessions,
            ApplicationDbContext db,
            DictionaryAccessService access,
            ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            if (!await access.IsVisibleAsync(request.DictionaryId, userId, role))
            {
                return Results.NotFound();
            }

            var training = await sessions.StartNewBatchAsync(
                userId, request.DictionaryId, DateTime.UtcNow, request.ChapterIds, request.BatchSize, request.WordPairIds);

            return training == null
                ? Results.NoContent()
                : Results.Created($"/api/training/{training.Id}", await StartedAsync(sessions, db, training));
        });

        group.MapPost("/review", async (
            ReviewRequest? request,
            TrainingSessionService sessions,
            ApplicationDbContext db,
            DictionaryAccessService access,
            ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            if (request != null && !await access.IsVisibleAsync(request.DictionaryId, userId, role))
            {
                return Results.NotFound();
            }

            var training = await sessions.StartReviewAsync(
                userId, DateTime.UtcNow, request?.DictionaryId, request?.ChapterIds);

            return training == null
                ? Results.NoContent()
                : Results.Created($"/api/training/{training.Id}", await StartedAsync(sessions, db, training));
        });

        group.MapPost("/{id:long}/retry", async (
            long id, TrainingSessionService sessions, ApplicationDbContext db, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            var training = await sessions.StartRetryAsync(userId, id, DateTime.UtcNow);

            return training == null
                ? Results.NoContent()
                : Results.Created($"/api/training/{training.Id}", await StartedAsync(sessions, db, training));
        });

        group.MapGet("/{id:long}/next", async (
            long id, TrainingSessionService sessions, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            var view = await sessions.GetNextQuestionViewAsync(id);

            return Results.Ok(new NextQuestion(ToDto(view.Question, view.Options), view.Answered, view.Total));
        });

        group.MapPost("/{id:long}/answer", async (
            long id,
            AnswerRequest request,
            TrainingSessionService sessions,
            ApplicationDbContext db,
            ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            if (await sessions.FindAsync(id, userId) == null || await BelongsToAnotherSessionAsync(db, request.QuestionId, id))
            {
                return Results.NotFound();
            }

            var outcome = await sessions.AnswerAsync(request.QuestionId, request.PickedWordPairId, DateTime.UtcNow);

            // The SPA grades in the browser — the question DTO carries its own wordPairId — and
            // posts here only so the answer is on record for the session's Leitner grading; it
            // reads nothing from this response. The verdict is still returned for any other client.
            // null — the question is already answered (double click) or gone: the client just moves on to the next one.
            return outcome == null
                ? Results.NoContent()
                : Results.Ok(new AnswerResult(outcome.IsCorrect, outcome.Word.Id, outcome.Word.Word, outcome.Word.Translation));
        });

        group.MapPost("/{id:long}/known", async (
            long id,
            KnownRequest request,
            TrainingSessionService sessions,
            ApplicationDbContext db,
            ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            if (await sessions.FindAsync(id, userId) == null || await BelongsToAnotherSessionAsync(db, request.QuestionId, id))
            {
                return Results.NotFound();
            }

            var word = await sessions.MarkKnownAsync(request.QuestionId, DateTime.UtcNow);

            return word == null ? Results.NoContent() : Results.Ok(new KnownResult(word.Word));
        });

        group.MapPost("/{id:long}/finish", async (
            long id, TrainingSessionService sessions, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await sessions.FinishAsync(id, DateTime.UtcNow));
        });
    }

    private static async Task<TrainingStarted> StartedAsync(
        TrainingSessionService sessions, ApplicationDbContext db, Training training)
    {
        var words = await sessions.GetBatchWordsAsync(training.Id);
        var totalQuestions = await db.TrainingQuestions.CountAsync(q => q.TrainingId == training.Id);

        return new TrainingStarted(
            training.Id,
            training.Mode,
            words.Select(w => new BatchWord(w.Id, w.Word, w.Translation)).ToList(),
            totalQuestions);
    }

    /// <summary>
    /// A question that no longer exists (removed via the "Know" button) is a race, not a client
    /// error: hand it to the service and it answers null → 204. Someone else's question is a 404.
    /// </summary>
    private static async Task<bool> BelongsToAnotherSessionAsync(ApplicationDbContext db, long questionId, long trainingId)
    {
        var owner = await db.TrainingQuestions
            .Where(q => q.Id == questionId)
            .Select(q => (long?)q.TrainingId)
            .FirstOrDefaultAsync();

        return owner is { } other && other != trainingId;
    }

    private static QuestionDto? ToDto(TrainingQuestion? question, IReadOnlyList<WordPair> options)
    {
        if (question == null)
        {
            return null;
        }

        // The direction only decides which side of the pair goes in the question and which on the buttons.
        var enToUa = question.Direction == QuestionDirection.EnToUa;
        var prompt = enToUa ? question.WordPair.Word : question.WordPair.Translation;

        var labels = options
            .Select(o => new QuestionOption(o.Id, enToUa ? o.Translation : o.Word))
            .ToList();

        return new QuestionDto(question.Id, question.WordPairId, question.Direction, prompt, labels);
    }
}
