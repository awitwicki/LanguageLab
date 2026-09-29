using LanguageLab.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LanguageLab.Tests;

public class CurrentLanguageTests
{
    private static HttpCurrentLanguage With(string? code)
    {
        var http = new DefaultHttpContext();
        http.Items[HttpCurrentLanguage.ItemKey] = code;
        return new HttpCurrentLanguage(new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public void The_language_comes_from_the_request_items()
    {
        Assert.Equal("pl", With("pl").Get()!.Code);
    }

    [Fact]
    public void No_language_and_a_foreign_code_both_read_as_not_set()
    {
        Assert.Null(With(null).Get());
        Assert.Null(With("ru").Get());
        Assert.Throws<LanguageNotSetException>(() => With(null).Require());
    }

    [Fact]
    public async Task The_middleware_turns_a_missing_language_into_409()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await LanguageNotSetMiddleware.InvokeAsync(http, () => throw new LanguageNotSetException());

        Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        Assert.Contains("language_not_set", await new StreamReader(http.Response.Body).ReadToEndAsync());
    }
}
