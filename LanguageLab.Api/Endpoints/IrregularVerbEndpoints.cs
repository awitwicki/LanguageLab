using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Api.Endpoints;

/// <summary>One judged card, as the browser reports it.</summary>
public sealed record VerbAnswerRequest(
    string Verb, PromptForm PromptForm, bool Known, int ResponseMs, DrillMode Mode, int? Group);

/// <summary>
/// Thin layer over the two trainer services: the current user from the cookie, query-string
/// parsing, and status codes. No learning logic.
/// </summary>
public static class IrregularVerbEndpoints
{
    public static void MapIrregularVerbEndpoints(this WebApplication app)
    {
        var verbs = app.MapGroup("/api/irregular-verbs").RequireAuthorization();

        verbs.MapGet("/progress", async (VerbKnowledgeService knowledge, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();

            return Results.Ok(await knowledge.GetAsync(userId));
        });

        verbs.MapGet("/next", async (
            string? mode, int? group, string? scope, string? exclude,
            VerbDrillService drill, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var request = ParseDrill(mode, group, scope, exclude);

            if (request == null)
            {
                return Results.BadRequest();
            }

            var card = await drill.NextAsync(userId, request);

            // No card means a batch run has passed every verb of its stage.
            return card == null ? Results.NoContent() : Results.Ok(card);
        });

        verbs.MapPost("/answers", async (
            VerbAnswerRequest request, VerbKnowledgeService knowledge, ICurrentUserContext currentUser) =>
        {
            if (!IsValidAnswer(request))
            {
                return Results.BadRequest();
            }

            var (userId, _) = currentUser.Require();

            var result = await knowledge.ApplyAsync(
                userId, request.Verb, request.PromptForm, request.Known, request.ResponseMs,
                request.Mode, request.Group, DateTime.UtcNow);

            return result == null ? Results.NotFound() : Results.Ok(result);
        });
    }

    /// <summary>
    /// `POST /answers`'s body is ordinary JSON, not a query string with its own casing
    /// problem — but a stale or hand-crafted client can still send a verb-less body, or an
    /// out-of-range enum (the app's `JsonStringEnumConverter` accepts integers by default,
    /// so `promptForm: 7` deserializes instead of failing), straight into the append-only
    /// answer log. True means the request is coherent enough to apply.
    /// </summary>
    public static bool IsValidAnswer(VerbAnswerRequest request) =>
        !string.IsNullOrEmpty(request.Verb)
        && Enum.IsDefined(request.PromptForm)
        && Enum.IsDefined(request.Mode)
        && (request.Group is null || (request.Group >= 1 && request.Group <= IrregularVerbCatalog.GroupCount));

    /// <summary>
    /// Reads `GET /next`'s query string by hand — minimal API binds a query enum
    /// case-sensitively, so the SPA's lowercase `batch` would 400 — and refuses the
    /// combinations that have no meaning. Null means 400.
    /// </summary>
    public static DrillRequest? ParseDrill(string? mode, int? group, string? scope, string? exclude)
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
            // A batch run is always one stage, so a scope would be a contradiction.
            return group == null || scope != null
                ? null
                : new DrillRequest(DrillMode.Batch, group, DrillScope.Stage, exclude);
        }

        return scope?.ToLowerInvariant() switch
        {
            "stage" when group != null => new DrillRequest(DrillMode.Free, group, DrillScope.Stage, exclude),
            "cumulative" when group is > 1 => new DrillRequest(DrillMode.Free, group, DrillScope.Cumulative, exclude),
            "all" => new DrillRequest(DrillMode.Free, null, DrillScope.All, exclude),
            _ => null,
        };
    }
}
