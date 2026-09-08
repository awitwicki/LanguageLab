using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using LanguageLab.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Api.Endpoints;

public sealed record NewBatchRequest(
    long DictionaryId, IReadOnlyList<long>? ChapterIds, int BatchSize, IReadOnlyList<long>? WordPairIds);

public sealed record BatchPreview(LearningProgress Learning, int LearnableCount, IReadOnlyList<Candidate> Candidates);

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
/// Тонкий шар над TrainingSessionService: перевірка власності сесії, мапінг у DTO і коди
/// відповідей — жодної логіки навчання. Черга питань живе в БД, тож клієнт може
/// перезавантажитись посеред квізу, не втративши стан сесії.
/// </summary>
public static class TrainingEndpoints
{
    public static void MapTrainingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/training");

        // Превью для екрана старту: шкала скоупу + топ кандидатів за частотою. Те, що тут показано,
        // клієнт потім передає в new-batch явними id. Неіснуючий словник дає порожнє превью, як і new-batch дає 204.
        group.MapGet("/preview", async (
            long dictionaryId,
            string? chapterIds,
            int? take,
            WordSelectionService selection,
            LearningProgressService learningProgress,
            ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            var chapters = QueryParsing.ParseChapterIds(chapterIds);

            var learning = await learningProgress.GetAsync(userId, dictionaryId, chapters);
            var learnable = await selection.CountLearnableAsync(userId, dictionaryId, chapters);
            var candidates = await selection.GetCandidatesAsync(
                userId, dictionaryId, chapters, take ?? WordSelectionService.MaxCandidates);

            return Results.Ok(new BatchPreview(learning, learnable, candidates));
        });

        group.MapPost("/new-batch", async (
            NewBatchRequest request,
            TrainingSessionService sessions,
            ApplicationDbContext db,
            ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();

            var training = await sessions.StartNewBatchAsync(
                userId, request.DictionaryId, DateTime.UtcNow, request.ChapterIds, request.BatchSize, request.WordPairIds);

            return training == null
                ? Results.NoContent()
                : Results.Created($"/api/training/{training.Id}", await StartedAsync(sessions, db, training));
        });

        group.MapPost("/review", async (
            TrainingSessionService sessions, ApplicationDbContext db, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            var training = await sessions.StartReviewAsync(userId, DateTime.UtcNow);

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

            // null — питання вже відповідане (подвійний клік) або зникло: клієнт просто йде за наступним.
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
    /// Питання, якого вже немає (зняте кнопкою «Знаю»), — це не помилка клієнта, а гонка:
    /// віддаємо сервісу, і він відповість null → 204. Чуже ж питання — 404.
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

        // Напрямок вирішує лише, який бік пари показати в питанні, а який — на кнопках.
        var enToUa = question.Direction == QuestionDirection.EnToUa;
        var prompt = enToUa ? question.WordPair.Word : question.WordPair.Translation;

        var labels = options
            .Select(o => new QuestionOption(o.Id, enToUa ? o.Translation : o.Word))
            .ToList();

        return new QuestionDto(question.Id, question.WordPairId, question.Direction, prompt, labels);
    }
}
