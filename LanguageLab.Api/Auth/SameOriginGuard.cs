namespace LanguageLab.Api.Auth;

/// <summary>
/// CSRF defence. The session cookie is SameSite=Lax, which stops other sites' POSTs but not other
/// *.kodzuverse.com subdomains' (same-site), and not a cross-site top-level GET — and some GETs
/// here do work (a reader lookup can call the model). Browsers say where a request came from in
/// Sec-Fetch-Site; anything but the app itself ("same-origin") or a typed URL ("none") is refused.
/// A browser too old to send it falls back to comparing Origin on writes. The OIDC callback is the
/// one route that legitimately arrives from another site.
/// </summary>
public static class SameOriginGuard
{
    private static readonly PathString Callback = "/api/auth/telegram/callback";

    public static bool IsAllowed(
        string method, PathString path, string? fetchSite, string? origin, string scheme, HostString host)
    {
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments(Callback))
        {
            return true;
        }

        if (fetchSite is not null)
        {
            return fetchSite is "same-origin" or "none";
        }

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return true;
        }

        return origin is null || string.Equals(origin, $"{scheme}://{host}", StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseSameOriginGuard(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var request = context.Request;

            if (IsAllowed(request.Method, request.Path, request.Headers["Sec-Fetch-Site"].FirstOrDefault(),
                    request.Headers.Origin.FirstOrDefault(), request.Scheme, request.Host))
            {
                return next(context);
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        });
}

/// <summary>
/// The session cookie's name. Outside Development the __Host- prefix makes the browser refuse any
/// copy of it set by a sibling subdomain (Domain=) or over http — no session fixation from another
/// *.kodzuverse.com app. Development runs on http://localhost, where the prefix cannot work.
/// </summary>
public static class SessionCookie
{
    public static string NameFor(bool isDevelopment) => isDevelopment ? "ll_session" : "__Host-ll_session";
}
