using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Api.Endpoints;

/// <summary>One judged card, as the browser reports it.</summary>
public sealed record VerbAnswerRequest(
    string Verb, PromptForm PromptForm, bool Known, int ResponseMs, DrillMode Mode, int? Group);

/// <summary>A chunk of judged cards, in the order they were answered.</summary>
public sealed record VerbAnswersRequest(IReadOnlyList<VerbAnswerRequest>? Answers);

/// <summary>Where each answer of a chunk left its verb, in the order the chunk listed them.</summary>
public sealed record VerbAnswersResponse(IReadOnlyList<AnswerResultView> Results);

/// <summary>
/// Thin layer over the two trainer services: the current user from the cookie, query-string
/// parsing, and status codes. No learning logic.
/// </summary>
public static class IrregularVerbEndpoints
{
    /// <summary>
    /// How many answers one request may carry. A session's whole run is a few dozen cards, so
    /// this is room to spare rather than a limit the browser works around.
    /// </summary>
    public const int MaxAnswersPerRequest = 100;

    public static void MapIrregularVerbEndpoints(this WebApplication app)
    {
        var verbs = app.MapGroup("/api/irregular-verbs").RequireAuthorization();

        verbs.MapGet("/progress", async (VerbKnowledgeService knowledge, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();

            return Results.Ok(await knowledge.GetAsync(userId));
        });

        verbs.MapGet("/session", async (
            string? mode, int? group, string? scope,
            VerbSessionService sessions, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var request = ParseSession(mode, group, scope);

            if (request == null)
            {
                return Results.BadRequest();
            }

            var session = await sessions.StartAsync(userId, request);

            // No session means ordinary training has passed every verb of its stage.
            return session == null ? Results.NoContent() : Results.Ok(session);
        });

        verbs.MapPost("/answers", async (
            VerbAnswersRequest request, VerbKnowledgeService knowledge, ICurrentUserContext currentUser) =>
        {
            if (!IsValidChunk(request))
            {
                return Results.BadRequest();
            }

            var (userId, _) = currentUser.Require();

            var results = await knowledge.ApplyManyAsync(
                userId,
                request.Answers!
                    .Select(a => new VerbAnswerToApply(
                        a.Verb, a.PromptForm, a.Known, a.ResponseMs, a.Mode, a.Group))
                    .ToList(),
                DateTime.UtcNow);

            return Results.Ok(new VerbAnswersResponse(results));
        });
    }

    /// <summary>
    /// True when every answer of the chunk can be applied. The chunk is all-or-nothing: the
    /// browser retries a failed request, and with no answer identity to deduplicate against, a
    /// half-applied chunk would count its first answers twice — so a single bad entry refuses
    /// the whole body and nothing is written. Catalog membership is checked here rather than
    /// coming back from the service as a 404, because one unknown verb among a hundred good
    /// answers is a broken client, not a missing resource.
    /// </summary>
    public static bool IsValidChunk(VerbAnswersRequest request) =>
        request.Answers is { Count: > 0 and <= MaxAnswersPerRequest }
        && request.Answers.All(a => IsValidAnswer(a) && IrregularVerbCatalog.Find(a.Verb) != null);

    /// <summary>
    /// One answer of a chunk. The body is ordinary JSON, not a query string with its own casing
    /// problem — but a stale or hand-crafted client can still send a verb-less entry, or an
    /// out-of-range enum (the app's `JsonStringEnumConverter` accepts integers by default, so
    /// `promptForm: 7` deserializes instead of failing), straight into the append-only answer
    /// log. True means the entry is coherent enough to apply.
    /// </summary>
    public static bool IsValidAnswer(VerbAnswerRequest request) =>
        !string.IsNullOrEmpty(request.Verb)
        && Enum.IsDefined(request.PromptForm)
        && Enum.IsDefined(request.Mode)
        && (request.Group is null || (request.Group >= 1 && request.Group <= IrregularVerbCatalog.GroupCount));

    /// <summary>
    /// Reads `GET /session`'s query string by hand — minimal API binds a query enum
    /// case-sensitively, so the SPA's lowercase `batch` would 400 — and refuses the combinations
    /// that have no meaning. Null means 400.
    /// </summary>
    public static SessionRequest? ParseSession(string? mode, int? group, string? scope)
    {
        if (group is not null && (group < 1 || group > IrregularVerbCatalog.GroupCount))
        {
            return null;
        }

        var parsedMode = mode?.ToLowerInvariant() switch
        {
            "batch" => DrillMode.Batch,
            "free" => DrillMode.Free,
            _ => (DrillMode?)null,
        };

        if (parsedMode == null)
        {
            return null;
        }

        if (parsedMode == DrillMode.Batch)
        {
            // Ordinary training is always one stage, so a scope would be a contradiction.
            return group == null || scope != null
                ? null
                : new SessionRequest(DrillMode.Batch, group, DrillScope.Stage);
        }

        return scope?.ToLowerInvariant() switch
        {
            "stage" when group != null => new SessionRequest(DrillMode.Free, group, DrillScope.Stage),
            "cumulative" when group is > 1 => new SessionRequest(DrillMode.Free, group, DrillScope.Cumulative),
            "all" => new SessionRequest(DrillMode.Free, null, DrillScope.All),
            _ => null,
        };
    }
}
