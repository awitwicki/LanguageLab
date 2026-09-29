using LanguageLab.Application.Services;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;

namespace LanguageLab.Api.Endpoints;

/// <summary>
/// DictionaryId — where the client was sorting, so the home screen can offer a way back.
/// Optional: the mark itself needs no scope, and an older client sends none.
/// </summary>
public sealed record MarkRequest(long WordPairId, SortStatus Status, long? DictionaryId, long? ChapterId);

public static class SortingEndpoints
{
    public static void MapSortingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sorting").RequireAuthorization();

        group.MapGet("/queue", async (
            long dictionaryId,
            string? chapterIds,
            int? take,
            WordSortingService sorting,
            DictionaryAccessService access,
            ICurrentUserContext currentUser,
            ICurrentLanguage language) =>
        {
            var (userId, role) = currentUser.Require();

            if (!await access.IsVisibleAsync(dictionaryId, userId, role))
            {
                return Results.NotFound();
            }

            var chapters = QueryParsing.ParseChapterIds(chapterIds);

            var queue = await sorting.GetQueueAsync(
                userId, language.Require().Code, dictionaryId, chapters, take ?? WordSortingService.DefaultTake);

            return Results.Ok(queue);
        });

        group.MapPost("/mark", async (
            MarkRequest request, WordSortingService sorting, TranslationService translation,
            ICurrentUser currentUser, ICurrentLanguage language, CancellationToken cancellationToken) =>
        {
            var userId = await currentUser.GetIdAsync();

            // A chapter without its book names no scope — the visit is dropped rather than guessed at.
            var scope = request.DictionaryId is { } dictionaryId
                ? new SortingScope(dictionaryId, request.ChapterId)
                : null;

            var marked = await sorting.MarkAsync(userId, request.WordPairId, request.Status, DateTime.UtcNow, scope);

            // language.Get(), not Require(): the mark above already committed, so a learner with
            // no language set yet (a race the SPA's picker should prevent, but not a certainty)
            // must not turn an already-saved mark into a 409 — final review, finding 4.
            if (marked)
            {
                await TranslateIfUnknownAsync(
                    sorting, translation, request.Status, request.WordPairId, userId, language.Get(), cancellationToken);
            }

            // 404 rather than 403: an unknown id and someone else's private word look the
            // same from here, so neither is probeable by id.
            return marked ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/undo", async (WordSortingService sorting, ICurrentUser currentUser, ICurrentLanguage language) =>
        {
            var userId = await currentUser.GetIdAsync();
            var undone = await sorting.UndoAsync(userId, language.Require().Code);

            return undone == null ? Results.NoContent() : Results.Ok(undone);
        });

        group.MapGet("/recent", async (
            int? take, WordSortingService sorting, ICurrentUser currentUser) =>
        {
            var userId = await currentUser.GetIdAsync();
            return Results.Ok(await sorting.GetRecentAsync(userId, take ?? 10));
        });
    }

    /// <summary>
    /// On a fresh or repeated "don't know" mark, translates the shared word into language if it
    /// has none there yet — B2's Q6 "don't know" trigger. No-op for Known/Excluded, a personal
    /// word, one already translated, or a caller with no language set yet. A miss inside the
    /// user's uncached-translation window leaves the word untranslated, like a model with no answer.
    /// </summary>
    public static async Task TranslateIfUnknownAsync(
        WordSortingService sorting, TranslationService translation, SortStatus status, long wordPairId,
        long userId, LearnerLanguage? language, CancellationToken cancellationToken)
    {
        if (status != SortStatus.Unknown || language is null)
        {
            return;
        }

        if (await sorting.SharedWordNeedingTranslationAsync(wordPairId, language.Code) is { } word)
        {
            await translation.LookupAsync(userId, word, language, cancellationToken);
        }
    }
}
