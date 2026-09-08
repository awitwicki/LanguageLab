using LanguageLab.Api.Auth;
using LanguageLab.Domain.Entities;

namespace LanguageLab.Api;

/// <summary>
/// Who is working right now. Backed by the session cookie's claims, so it costs no database
/// round-trip — SessionValidator has already confirmed the user on this request.
/// </summary>
public interface ICurrentUser
{
    Task<long> GetIdAsync();
}

public sealed record CurrentUserContext(long Id, UserRole Role);

/// <summary>For the places that need the role too: the visibility filter and the admin guard.</summary>
public interface ICurrentUserContext
{
    CurrentUserContext? Get();
}

public static class CurrentUserContextExtensions
{
    /// <summary>
    /// Every caller sits behind RequireAuthorization, so an anonymous principal here is a
    /// wiring bug, not a user error — fail loudly rather than silently acting as nobody.
    /// </summary>
    public static CurrentUserContext Require(this ICurrentUserContext context) =>
        context.Get() ?? throw new InvalidOperationException(
            "An anonymous request reached an endpoint that requires authentication.");
}

public class ClaimsCurrentUser : ICurrentUser, ICurrentUserContext
{
    private readonly IHttpContextAccessor _accessor;

    public ClaimsCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public CurrentUserContext? Get()
    {
        var principal = _accessor.HttpContext?.User;

        return principal?.Identity?.IsAuthenticated == true ? PrincipalFactory.Read(principal) : null;
    }

    public Task<long> GetIdAsync() => Task.FromResult(this.Require().Id);
}
