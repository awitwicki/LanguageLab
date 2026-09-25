using System.Net;
using System.Text;
using LanguageLab.Application.Translation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class DeepLTranslatorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            // Read here: the translator disposes the request (and its content) once it returns.
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request);
        }
    }

    private static DeepLTranslator Translator(StubHandler handler, string? key = "secret:fx") =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(DeepLTranslator.BaseUrl) },
            Options.Create(new TranslationOptions { DeepLApiKey = key }),
            NullLogger<DeepLTranslator>.Instance);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Answer = """{"translations":[{"detected_source_language":"EN","text":"Усі вони мали певні труднощі."}]}""";

    [Fact]
    public async Task Sends_the_key_and_the_language_pair_and_parses_the_translation()
    {
        var handler = new StubHandler(_ => Json(Answer));

        var result = await Translator(handler).TranslateAsync("All of them had some difficulty.", CancellationToken.None);

        Assert.Equal(SentenceTranslation.Success("Усі вони мали певні труднощі."), result);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/v2/translate", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("DeepL-Auth-Key secret:fx", handler.LastRequest.Headers.GetValues("Authorization").Single());
        Assert.Contains("\"source_lang\":\"EN\"", handler.LastBody);
        Assert.Contains("\"target_lang\":\"UK\"", handler.LastBody);
        Assert.Contains("All of them had some difficulty.", handler.LastBody);
    }

    [Fact]
    public async Task Status_456_is_an_exhausted_quota()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)456));

        Assert.Equal(SentenceTranslation.Quota, await Translator(handler).TranslateAsync("Hi.", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, Answer)]
    [InlineData(HttpStatusCode.Forbidden, Answer)]
    [InlineData(HttpStatusCode.OK, """{"translations":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"translations":[{"text":"  "}]}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Anything_else_is_a_failure(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(_ => Json(body, status));

        Assert.Equal(SentenceTranslation.Failure, await Translator(handler).TranslateAsync("Hi.", CancellationToken.None));
    }

    [Fact]
    public async Task A_network_error_is_a_failure()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        Assert.Equal(SentenceTranslation.Failure, await Translator(handler).TranslateAsync("Hi.", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_key_nothing_is_sent(string? key)
    {
        var handler = new StubHandler(_ => Json(Answer));
        var translator = Translator(handler, key);

        Assert.False(translator.IsConfigured);
        Assert.Equal(SentenceTranslation.Failure, await translator.TranslateAsync("Hi.", CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }
}
