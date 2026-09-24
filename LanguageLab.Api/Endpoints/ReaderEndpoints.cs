using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain;

namespace LanguageLab.Api.Endpoints;

public sealed record ReaderCapabilities(bool SentenceTranslation);

public sealed record RegisterReaderBookRequest(string? Title, string? Author, int ChaptersCount);

public sealed record ReaderPositionRequest(
    int ChapterIndex, int ParagraphIndex, int SentenceIndex, double Progress, DateTime ClientUpdatedAt);

public sealed record LearnWordRequest(string? Translation);

/// <summary>
/// The reader's server side. The book file never comes here: only its hash, title and the
/// reading position. A malformed hash is a 400; someone else's or an unknown book a 404.
/// </summary>
public static class ReaderEndpoints
{
    public static void MapReaderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reader").RequireAuthorization();

        group.MapGet("/capabilities", (ISentenceTranslator sentences) =>
            Results.Ok(new ReaderCapabilities(sentences.IsConfigured)));

        group.MapGet("/books", async (ReaderBookService books, ICurrentUserContext currentUser) =>
        {
            var (userId, role) = currentUser.Require();

            return Results.Ok(await books.ListAsync(userId, role));
        });

        // PUT: opening the same file again is the same registration, so a retry is harmless.
        group.MapPut("/books/{hash}", async (
            string hash, RegisterReaderBookRequest request, ReaderBookService books, ICurrentUserContext currentUser) =>
        {
            if (ReaderHash.Normalize(hash) is not { } fileHash)
            {
                return Results.BadRequest();
            }

            var (userId, role) = currentUser.Require();

            try
            {
                return Results.Ok(await books.RegisterAsync(
                    userId, role, fileHash, request.Title ?? string.Empty, request.Author ?? string.Empty,
                    request.ChaptersCount, DateTime.UtcNow));
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("/books/{hash}/position", async (
            string hash, ReaderPositionRequest request, ReaderBookService books, ICurrentUser currentUser) =>
        {
            if (ReaderHash.Normalize(hash) is not { } fileHash)
            {
                return Results.BadRequest();
            }

            var position = new ReaderPosition(
                request.ChapterIndex, request.ParagraphIndex, request.SentenceIndex, request.Progress);

            return await books.SavePositionAsync(
                    await currentUser.GetIdAsync(), fileHash, position, request.ClientUpdatedAt, DateTime.UtcNow)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapDelete("/books/{hash}", async (string hash, ReaderBookService books, ICurrentUser currentUser) =>
        {
            if (ReaderHash.Normalize(hash) is not { } fileHash)
            {
                return Results.BadRequest();
            }

            return await books.RemoveAsync(await currentUser.GetIdAsync(), fileHash)
                ? Results.NoContent()
                : Results.NotFound();
        });

        group.MapGet("/word-statuses", async (ReaderWordStatusService statuses, ICurrentUser currentUser) =>
            Results.Ok(await statuses.GetAsync(await currentUser.GetIdAsync())));

        group.MapGet("/words/{lemma}", async (
            string lemma, ReaderWordService words, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var word = WordText.Normalize(lemma);

            return WordText.IsValid(word)
                ? Results.Ok(await words.GetAsync(await currentUser.GetIdAsync(), word, cancellationToken))
                : Results.BadRequest();
        });

        group.MapPost("/words/{lemma}/learn", async (
            string lemma, LearnWordRequest request, ReaderWordService words, ICurrentUser currentUser) =>
        {
            var word = WordText.Normalize(lemma);

            if (!WordText.IsValid(word))
            {
                return Results.BadRequest();
            }

            try
            {
                await words.LearnAsync(await currentUser.GetIdAsync(), word, request.Translation, DateTime.UtcNow);
                return Results.NoContent();
            }
            catch (ArgumentException e)
            {
                return Results.Json(new DictionaryError(e.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/words/{lemma}/known", async (string lemma, ReaderWordService words, ICurrentUser currentUser) =>
        {
            var word = WordText.Normalize(lemma);

            if (!WordText.IsValid(word))
            {
                return Results.BadRequest();
            }

            return await words.MarkKnownAsync(await currentUser.GetIdAsync(), word, DateTime.UtcNow)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }
}
