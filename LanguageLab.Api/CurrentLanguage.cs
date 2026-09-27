using LanguageLab.Domain.Languages;

namespace LanguageLab.Api;

/// <summary>An endpoint needed the learner's language and they have not picked one yet.</summary>
public sealed class LanguageNotSetException : Exception
{
    public LanguageNotSetException() : base("The learner has not picked a language yet.") { }
}

public interface ICurrentLanguage
{
    /// <summary>Null until the learner picks one — only ever a brand-new account.</summary>
    LearnerLanguage? Get();
}

public static class CurrentLanguageExtensions
{
    /// <summary>The SPA shows the picker first, so this throwing is a race, answered 409 by the middleware.</summary>
    public static LearnerLanguage Require(this ICurrentLanguage language) =>
        language.Get() ?? throw new LanguageNotSetException();
}

/// <summary>
/// Reads what SessionValidator put in HttpContext.Items: it loads the user row on every
/// request anyway, so the language costs no second query and a change applies to the very
/// next request.
/// </summary>
public class HttpCurrentLanguage : ICurrentLanguage
{
    public const string ItemKey = "LearnerLanguage";

    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentLanguage(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public LearnerLanguage? Get() =>
        _accessor.HttpContext?.Items[ItemKey] is string code ? LearnerLanguages.Find(code) : null;
}

public sealed record LanguageError(string Error);

public static class LanguageNotSetMiddleware
{
    public static async Task InvokeAsync(HttpContext http, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (LanguageNotSetException) when (!http.Response.HasStarted)
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            await http.Response.WriteAsJsonAsync(new LanguageError("language_not_set"));
        }
    }

    public static IApplicationBuilder UseLanguageNotSet(this IApplicationBuilder app) =>
        app.Use((http, next) => InvokeAsync(http, () => next()));
}
