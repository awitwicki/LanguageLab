using LanguageLab.Application.Services;
using LanguageLab.Domain.IrregularVerbs;

namespace LanguageLab.Api.Endpoints;

public sealed record GradeRequest(string Verb, VerbForm Form, bool Correct);

/// <summary>
/// Thin layer over IrregularVerbService: no learning logic here, just the current user
/// (from the cookie) and 404s for an unknown step or verb. A form outside v1 / v2 / v3
/// fails enum binding and is a 400 before the handler runs.
/// </summary>
public static class IrregularVerbEndpoints
{
    public static void MapIrregularVerbEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/irregular-verbs").RequireAuthorization();

        group.MapGet("/", async (IrregularVerbService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            return Results.Ok(await service.GetOverviewAsync(userId));
        });

        group.MapGet("/steps/{step:int}/session", async (
            int step, IrregularVerbService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var session = await service.GetSessionAsync(userId, step);

            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapPost("/grades", async (
            GradeRequest request, IrregularVerbService service, ICurrentUserContext currentUser) =>
        {
            var (userId, _) = currentUser.Require();
            var result = await service.GradeAsync(userId, request.Verb, request.Form, request.Correct, DateTime.UtcNow);

            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }
}
