using System.IO;
using System.Net;
using System.Text;
using LanguageLab.Application.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class MyMemoryTranslatorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly Action? _onRequest;

        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, Action? onRequest = null)
        {
            _respond = respond;
            _onRequest = onRequest;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            _onRequest?.Invoke();
            return Task.FromResult(_respond(request));
        }
    }

    // A generous default so the ~10 tests below, each doing a single short lookup, never come
    // anywhere near the new word budget by accident; only the budget test below overrides it.
    private static MyMemoryWordBudget GenerousBudget() =>
        new(Options.Create(new TranslationOptions()));

    private static MyMemoryTranslator Translator(StubHandler handler, string? email = null, MyMemoryWordBudget? budget = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(MyMemoryTranslator.BaseUrl) },
            Options.Create(new TranslationOptions { MyMemoryEmail = email }),
            budget ?? GenerousBudget(),
            NullLogger<MyMemoryTranslator>.Instance);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Apple = """{"responseData":{"translatedText":"яблуко","match":1},"quotaFinished":false,"responseStatus":200,"matches":[]}""";

    [Fact]
    public async Task Parses_the_translated_text()
    {
        var handler = new StubHandler(_ => Json(Apple));

        var result = await Translator(handler).TranslateAsync("apple", CancellationToken.None);

        Assert.Equal("яблуко", result);
        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("q=apple", query);
        Assert.Contains("langpair=en%7Cuk", query);
        Assert.DoesNotContain("de=", query);
    }

    [Fact]
    public async Task The_contact_email_rides_along_when_configured()
    {
        var handler = new StubHandler(_ => Json(Apple));

        await Translator(handler, "me@example.com").TranslateAsync("apple", CancellationToken.None);

        Assert.Contains("de=me%40example.com", handler.LastRequest!.RequestUri!.Query);
    }

    /// <summary>MyMemory reports an exhausted quota inside an HTTP 200 — as a number or as a string.</summary>
    [Theory]
    [InlineData("""{"responseData":{"translatedText":"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY"},"quotaFinished":true,"responseStatus":403}""")]
    [InlineData("""{"responseData":{"translatedText":"яблуко"},"responseStatus":"403"}""")]
    public async Task A_refusal_in_the_body_is_null(string body)
    {
        var handler = new StubHandler(_ => Json(body));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Fact]
    public async Task A_shouting_warning_with_status_200_is_null()
    {
        var handler = new StubHandler(_ => Json("""{"responseData":{"translatedText":"MYMEMORY WARNING: SOMETHING"},"responseStatus":200}"""));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    /// <summary>An untranslatable word comes back as itself; that is not a translation.</summary>
    [Fact]
    public async Task An_echo_of_the_word_is_null()
    {
        var handler = new StubHandler(_ => Json("""{"responseData":{"translatedText":"Apple"},"responseStatus":200}"""));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"responseData":{"translatedText":""},"responseStatus":200}""")]
    [InlineData("""{"responseData":{},"responseStatus":200}""")]
    [InlineData("""{"responseStatus":200}""")]
    [InlineData("not json at all")]
    public async Task An_empty_or_malformed_body_is_null(string body)
    {
        var handler = new StubHandler(_ => Json(body));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Fact]
    public async Task An_http_error_is_null()
    {
        var handler = new StubHandler(_ => Json("", HttpStatusCode.InternalServerError));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Fact]
    public async Task A_network_failure_is_null()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route"));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Fact]
    public async Task A_timeout_is_null()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    /// <summary>
    /// A connection dropped mid-transfer can surface as a plain IOException (or HttpIOException),
    /// neither of which is an HttpRequestException — this is not a hypothetical, it is why the
    /// catch clause has no `when` filter.
    /// </summary>
    [Fact]
    public async Task An_unanticipated_exception_is_also_null_not_thrown()
    {
        var handler = new StubHandler(_ => throw new IOException("connection reset"));

        Assert.Null(await Translator(handler).TranslateAsync("apple", CancellationToken.None));
    }

    [Fact]
    public async Task A_lookup_past_the_daily_budget_never_reaches_the_provider()
    {
        var calls = 0;
        var handler = new StubHandler(_ => Json(Apple), onRequest: () => calls++);
        var budget = new MyMemoryWordBudget(Options.Create(new TranslationOptions()));
        var translator = Translator(handler, budget: budget);

        // 3 000 characters is the anonymous word budget (5 000 * 0.6); spend it, then ask once more.
        for (var i = 0; i < 600; i++)
        {
            await translator.TranslateAsync("abcde", CancellationToken.None);
        }

        var spent = calls;
        var result = await translator.TranslateAsync("abcde", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(spent, calls);
    }
}
