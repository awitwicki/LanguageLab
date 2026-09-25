using System.Net;
using System.Text;
using LanguageLab.Application.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class MyMemorySentenceTranslatorTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static MyMemorySentenceTranslator Translator(
        StubHandler handler, string? email = null, MyMemorySentenceBudget? budget = null)
    {
        var options = Options.Create(new TranslationOptions { MyMemoryEmail = email });

        return new(
            new HttpClient(handler) { BaseAddress = new Uri(MyMemoryTranslator.BaseUrl) },
            options,
            budget ?? new MyMemorySentenceBudget(options),
            NullLogger<MyMemorySentenceTranslator>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Answer = """{"responseData":{"translatedText":"Усі вони мали певні труднощі."},"quotaFinished":false,"responseStatus":200}""";

    [Fact]
    public async Task Translates_a_sentence_english_to_ukrainian()
    {
        var handler = new StubHandler(_ => Json(Answer));

        var result = await Translator(handler, "me@example.com").TranslateAsync("All of them had some difficulty.", CancellationToken.None);

        Assert.Equal(SentenceTranslation.Success("Усі вони мали певні труднощі."), result);
        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("langpair=en%7Cuk", query);
        Assert.Contains("de=me%40example.com", query);
    }

    /// <summary>MyMemory refuses queries over 500 bytes; asking would only waste the daily quota.</summary>
    [Fact]
    public async Task A_sentence_over_500_bytes_is_too_long_and_nothing_is_sent()
    {
        var handler = new StubHandler(_ => Json(Answer));

        var result = await Translator(handler).TranslateAsync(new string('a', 501), CancellationToken.None);

        Assert.Equal(SentenceTranslation.TooLong, result);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("""{"responseData":{"translatedText":"MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY"},"quotaFinished":true,"responseStatus":403}""")]
    [InlineData("""{"responseData":{"translatedText":"x"},"responseStatus":"403"}""")]
    public async Task An_exhausted_daily_quota_is_reported_as_quota(string body)
    {
        var handler = new StubHandler(_ => Json(body));

        Assert.Equal(SentenceTranslation.Quota, await Translator(handler).TranslateAsync("Hi there.", CancellationToken.None));
    }

    [Fact]
    public async Task Status_429_is_quota()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        Assert.Equal(SentenceTranslation.Quota, await Translator(handler).TranslateAsync("Hi there.", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, Answer)]
    [InlineData(HttpStatusCode.OK, """{"responseData":{"translatedText":"MYMEMORY WARNING: SOMETHING"},"responseStatus":200}""")]
    [InlineData(HttpStatusCode.OK, """{"responseData":{"translatedText":"  "},"responseStatus":200}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Anything_else_is_a_failure(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(_ => Json(body, status));

        Assert.Equal(SentenceTranslation.Failure, await Translator(handler).TranslateAsync("Hi there.", CancellationToken.None));
    }

    [Fact]
    public async Task A_network_error_is_a_failure()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("reset"));

        Assert.Equal(SentenceTranslation.Failure, await Translator(handler).TranslateAsync("Hi there.", CancellationToken.None));
    }

    [Fact]
    public void It_needs_no_credentials()
    {
        Assert.True(Translator(new StubHandler(_ => Json(Answer))).IsConfigured);
    }

    /// <summary>The server-wide sentence budget guards MyMemory's daily quota, shared with the word lookups.</summary>
    [Fact]
    public async Task Once_the_daily_sentence_budget_is_spent_nothing_is_sent()
    {
        var handler = new StubHandler(_ => Json(Answer));
        var budget = new MyMemorySentenceBudget(Options.Create(new TranslationOptions()));
        Assert.True(budget.TryConsume(2_000, DateTime.UtcNow));

        var result = await Translator(handler, budget: budget).TranslateAsync("Hi there.", CancellationToken.None);

        Assert.Equal(SentenceTranslation.Quota, result);
        Assert.Equal(0, handler.Calls);
    }
}
