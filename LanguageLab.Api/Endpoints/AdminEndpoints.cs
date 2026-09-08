using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Api.Endpoints;

public sealed record RoleRequest(UserRole Role);

/// <summary>Shape of a refused action; the SPA shows `message` verbatim.</summary>
public sealed record AdminError(string Message);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        group.MapGet("/users", async (AdminUserService admin) => Results.Ok(await admin.ListAsync()));

        group.MapPost("/users/{id:long}/ban", async (
                long id, AdminUserService admin, ICurrentUserContext currentUser) =>
            Map(await admin.SetBannedAsync(currentUser.Require().Id, id, true)));

        group.MapPost("/users/{id:long}/unban", async (
                long id, AdminUserService admin, ICurrentUserContext currentUser) =>
            Map(await admin.SetBannedAsync(currentUser.Require().Id, id, false)));

        group.MapPost("/users/{id:long}/role", async (
                long id, RoleRequest request, AdminUserService admin, ICurrentUserContext currentUser) =>
            {
                if (!Enum.IsDefined(request.Role))
                {
                    return Results.BadRequest();
                }

                return Map(await admin.SetRoleAsync(currentUser.Require().Id, id, request.Role));
            });

        group.MapDelete("/users/{id:long}", async (
                long id, AdminUserService admin, ICurrentUserContext currentUser) =>
            Map(await admin.DeleteAsync(currentUser.Require().Id, id)));
    }

    /// <summary>
    /// A refused guard is a conflict, not a failure of authorisation: the caller is a
    /// legitimate admin, the state just does not allow this particular change.
    /// </summary>
    private static IResult Map(AdminActionResult result) => result switch
    {
        AdminActionResult.Ok => Results.NoContent(),
        AdminActionResult.NotFound => Results.NotFound(),
        AdminActionResult.SelfAction => Conflict("You cannot ban, demote or delete your own account."),
        AdminActionResult.LastAdmin => Conflict("This is the last administrator — promote someone else first."),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    private static IResult Conflict(string message) =>
        Results.Json(new AdminError(message), statusCode: StatusCodes.Status409Conflict);
}
