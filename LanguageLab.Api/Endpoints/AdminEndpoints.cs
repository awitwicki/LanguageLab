using LanguageLab.Api.Auth;
using LanguageLab.Application.Services;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Api.Endpoints;

public sealed record RoleRequest(UserRole Role);

/// <summary>Shape of a refused action; the SPA shows `message` verbatim.</summary>
public sealed record AdminError(string Message);

public sealed record DeletedDictionaries(int Count);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(AuthPolicies.Admin);

        // Optional query: ?search=&page=&pageSize=. The service clamps whatever arrives.
        group.MapGet("/users", async (
                string? search, int? page, int? pageSize, AdminUserService admin) =>
            Results.Ok(await admin.ListAsync(
                search, page ?? 1, pageSize ?? AdminUserService.DefaultPageSize)));

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

        // Used alongside a ban: the account stops, and so does everything it published.
        group.MapDelete("/users/{id:long}/dictionaries", async (long id, DictionaryDeletionService deletion) =>
            Results.Ok(new DeletedDictionaries(await deletion.DeleteOwnedAsync(id))));

        // The moderation queue. `status` defaults to what an admin opens this screen for.
        // Bound as a string, not `PublicationStatus?`: minimal API's own query-string binding
        // for an enum uses `Enum.TryParse` without `ignoreCase: true`, so it would 400 on the
        // lowercase `?status=pending` the SPA sends (matching this app's JSON enum convention
        // everywhere else) and only accept the exact-case `Pending`.
        group.MapGet("/dictionaries", async (
                string? status, int? page, int? pageSize, DictionaryPublicationService publication) =>
        {
            var parsed = ParseStatus(status);

            if (parsed is null)
            {
                return Results.BadRequest();
            }

            return Results.Ok(await publication.ListAsync(
                parsed.Value,
                page ?? 1,
                pageSize ?? DictionaryPublicationService.DefaultPageSize));
        });

        group.MapPost("/dictionaries/{id:long}/approve", async (long id, DictionaryPublicationService publication) =>
            await publication.SetStatusAsync(id, PublicationStatus.Published) == PublicationActionResult.Ok
                ? Results.NoContent()
                : Results.NotFound());

        group.MapPost("/dictionaries/{id:long}/reject", async (long id, DictionaryPublicationService publication) =>
            await publication.SetStatusAsync(id, PublicationStatus.Rejected) == PublicationActionResult.Ok
                ? Results.NoContent()
                : Results.NotFound());

        // The shelf admin panel: the calling admin's own words, never another user's — there is
        // no user picker here. `status` absent means the "All" list, not a default shelf, which
        // is why this cannot reuse ParseStatus above.
        group.MapGet("/shelf-words", async (
                string? status, string? search, int? page, int? pageSize,
                WordSortingService sorting, ICurrentUserContext currentUser) =>
        {
            if (!TryParseShelfStatus(status, out var parsed))
            {
                return Results.BadRequest();
            }

            return Results.Ok(await sorting.ListShelfWordsAsync(
                currentUser.Require().Id,
                parsed,
                search,
                page ?? 1,
                pageSize ?? WordSortingService.DefaultShelfPageSize));
        });
    }

    /// <summary>
    /// Parses the moderation queue's `status` query parameter case-insensitively. Null or empty
    /// means the screen's own default (<see cref="PublicationStatus.Pending"/>); anything else
    /// must name a real status, not a bare ordinal — `Enum.TryParse` alone would accept a
    /// numeric string like "9" even when no member has that value, so it is checked separately.
    /// Returns null when <paramref name="status"/> is present but invalid.
    /// </summary>
    public static PublicationStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
        {
            return PublicationStatus.Pending;
        }

        return Enum.TryParse<PublicationStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Parses the shelf admin panel's `status` query parameter case-insensitively. Unlike
    /// <see cref="ParseStatus"/>, absent here means "no filter" — the "All" list — rather than a
    /// default shelf, so <paramref name="status"/> being null is success with a null shelf, not
    /// the failure case. Returns false when <paramref name="status"/> is present but not a real
    /// shelf name.
    /// </summary>
    public static bool TryParseShelfStatus(string? status, out SortStatus? parsed)
    {
        if (string.IsNullOrEmpty(status))
        {
            parsed = null;
            return true;
        }

        if (Enum.TryParse<SortStatus>(status, ignoreCase: true, out var value) && Enum.IsDefined(value))
        {
            parsed = value;
            return true;
        }

        parsed = null;
        return false;
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
