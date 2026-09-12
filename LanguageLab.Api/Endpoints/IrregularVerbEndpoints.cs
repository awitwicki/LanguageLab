using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Api.Endpoints;

public sealed record StartSessionRequest(SessionMode Mode, string? Family, long? FromSessionId);

public sealed record SessionStartedDto(long Id, SessionMode Mode, int? Group, string? Family, string Title, int Total);

public sealed record VerbDto(string V1, string Translation, int Group, string Family, IReadOnlyList<string> Suffixes);

public sealed record ExampleDto(Tense Tense, string Text);

public sealed record CardDto(IReadOnlyList<string> V2, IReadOnlyList<string> V3, IReadOnlyList<ExampleDto> Examples, string? Note);

public sealed record GapChoiceDto(string Sentence, Tense Tense, IReadOnlyList<string> Options);

public sealed record OddOneDto(IReadOnlyList<string> Options);

public sealed record MatchDto(FormAsked Form, IReadOnlyList<string> Lefts, IReadOnlyList<string> Rights, IReadOnlyList<MatchPair> Matched);

public sealed record FormPickDto(string Sentence);

public sealed record GapTypeDto(string Sentence, Tense Tense, string Hint);

public sealed record TripleTypeDto(string V1, bool AutofillV3);

/// <summary>One task for the client: the verb, and exactly one exercise block, answers stripped.</summary>
public sealed record TaskDto(
    long Id,
    ExerciseType Type,
    FormAsked FormAsked,
    int Level,
    bool IsReturn,
    VerbDto Verb,
    CardDto? Card,
    GapChoiceDto? GapChoice,
    OddOneDto? OddOne,
    MatchDto? Match,
    FormPickDto? FormPick,
    GapTypeDto? GapType,
    TripleTypeDto? TripleType);

public sealed record NextTaskDto(TaskDto? Task, int Answered, int Total);

public sealed record AnswerTaskRequest(long TaskId, string Answer, int? ResponseMs);

public sealed record RefusalDto(string Message);

/// <summary>
/// Thin layer over the two trainer services: the current user from the cookie, session
/// ownership, DTO mapping that strips answers, and status codes. No learning logic.
/// </summary>
public static class IrregularVerbEndpoints
{
    public static void MapIrregularVerbEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/irregular-verbs").RequireAuthorization();

        group.MapGet("/progress", async (VerbProgressService progress, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            return Results.Ok(await progress.GetAsync(userId));
        });

        group.MapPost("/sessions", async (
            StartSessionRequest request, VerbSessionService sessions, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var result = await sessions.StartAsync(userId, request.Mode, request.Family, request.FromSessionId, DateTime.UtcNow);

            if (result.Session == null)
            {
                return Results.Conflict(new RefusalDto(result.Refusal!));
            }

            var session = result.Session;
            return Results.Created($"/api/irregular-verbs/sessions/{session.Id}", new SessionStartedDto(
                session.Id, session.Mode, session.Group, session.Family, TitleOf(session), session.Tasks.Count));
        });

        group.MapGet("/sessions/{id:long}/next", async (
            long id, VerbSessionService sessions, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            var next = await sessions.NextAsync(id);
            return Results.Ok(new NextTaskDto(next.Task == null ? null : ToDto(next.Task), next.Answered, next.Total));
        });

        group.MapPost("/sessions/{id:long}/answer", async (
            long id, AnswerTaskRequest request, VerbSessionService sessions, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            var view = await sessions.AnswerAsync(id, request.TaskId, request.Answer, request.ResponseMs, DateTime.UtcNow);

            // null — the task is already answered (a double click) or belongs elsewhere: the client just asks for the next one.
            return view == null ? Results.NoContent() : Results.Ok(view);
        });

        group.MapPost("/sessions/{id:long}/finish", async (
            long id, VerbSessionService sessions, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();

            if (await sessions.FindAsync(id, userId) == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await sessions.FinishAsync(id, DateTime.UtcNow));
        });

        group.MapPost("/verbs/{v1}/forgot", async (
            string v1, VerbProgressService progress, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var result = await progress.ForgotAsync(userId, v1, DateTime.UtcNow);

            return result == null ? Results.NotFound() : Results.Ok(result);
        });
    }

    public static string TitleOf(VerbSession session) =>
        session.Mode switch
        {
            SessionMode.Learn => IrregularVerbCatalog.FindFamily(session.Family!)?.Title ?? session.Family!,
            SessionMode.ErrorsOnly => "Errors only",
            _ => "Mixed session",
        };

    public static TaskDto ToDto(VerbTask task)
    {
        var verb = IrregularVerbCatalog.Find(task.Verb)!;
        var family = IrregularVerbCatalog.FamilyOf(verb);
        var payload = task.GetPayload();
        var verbDto = new VerbDto(verb.V1, verb.Translation, verb.Group, verb.Family, family.Suffixes);

        CardDto? card = null;
        GapChoiceDto? gapChoice = null;
        OddOneDto? oddOne = null;
        MatchDto? match = null;
        FormPickDto? formPick = null;
        GapTypeDto? gapType = null;
        TripleTypeDto? tripleType = null;

        switch (task.Type)
        {
            case ExerciseType.Card:
                card = new CardDto(verb.V2, verb.V3, verb.Examples.Select(e => new ExampleDto(e.Tense, e.Text)).ToList(), verb.Note);
                break;
            case ExerciseType.GapChoice:
                gapChoice = new GapChoiceDto(payload.Sentence!, payload.Tense!.Value, payload.Options!);
                break;
            case ExerciseType.OddOne:
                oddOne = new OddOneDto(payload.Options!);
                break;
            case ExerciseType.Match:
                match = new MatchDto(payload.Form!.Value, payload.Pairs!.Select(p => p.Left).ToList(), payload.RightOrder!, payload.Matched ?? []);
                break;
            case ExerciseType.FormPick:
                formPick = new FormPickDto(payload.Sentence!);
                break;
            case ExerciseType.GapType:
                gapType = new GapTypeDto(payload.Sentence!, payload.Tense!.Value, payload.Hint!);
                break;
            case ExerciseType.TripleType:
                tripleType = new TripleTypeDto(verb.V1, payload.AutofillV3 ?? false);
                break;
        }

        return new TaskDto(
            task.Id, task.Type, task.FormAsked, task.Level, task.IsReturn, verbDto,
            card, gapChoice, oddOne, match, formPick, gapType, tripleType);
    }
}
