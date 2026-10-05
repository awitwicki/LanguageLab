namespace LanguageLab.Api.Auth;

/// <summary>
/// Who did what with admin powers: every non-GET request to an admin endpoint is logged with the
/// actor's id, the route (ids included — no names or content) and the status it got. GETs are
/// reads and stay out of it.
/// </summary>
public static class AdminAudit
{
    public const string Category = "LanguageLab.Audit";

    public static void Write(ILogger logger, long? actorId, string method, string path, int? status) =>
        logger.LogInformation("Admin {ActorId} {Method} {Path} -> {Status}", actorId, method, path, status);

    public static TBuilder WithAdminAudit<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var http = context.HttpContext;

            if (!HttpMethods.IsGet(http.Request.Method))
            {
                var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
                var status = (result as IStatusCodeHttpResult)?.StatusCode ?? http.Response.StatusCode;
                Write(logger, PrincipalFactory.Read(http.User)?.Id, http.Request.Method, http.Request.Path, status);
            }

            return result;
        });
}
