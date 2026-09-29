using LanguageLab.Api;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;
using LanguageLab.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguageLab.Tests;

public class TranslateSentenceEndpointTests
{
    private sealed class FakeUser : ICurrentUser
    {
        public Task<long> GetIdAsync() => Task.FromResult(7L);
    }

    private sealed class FakeLanguage : ICurrentLanguage
    {
        public LearnerLanguage? Get() => LearnerLanguages.Find("pl");
    }

    private sealed class FakeSentences(bool configured, SentenceTranslation answer) : ISentenceTranslator
    {
        public int Calls { get; private set; }
        public LearnerLanguage? LastTarget { get; private set; }

        public bool IsConfigured => configured;

        public Task<SentenceTranslation> TranslateAsync(string text, LearnerLanguage target, CancellationToken cancellationToken)
        {
            Calls++;
            LastTarget = target;
            return Task.FromResult(answer);
        }
    }

    private static Task<IResult> Call(
        ISentenceTranslator translator, string? text, UncachedTranslationLimiter? limiter = null, HttpContext? http = null) =>
        TranslationEndpoints.TranslateSentenceAsync(
            new SentenceRequest(text),
            translator,
            limiter ?? new UncachedTranslationLimiter(TimeProvider.System),
            new FakeUser(),
            new FakeLanguage(),
            http ?? new DefaultHttpContext(),
            CancellationToken.None);

    [Fact]
    public async Task A_translation_comes_back()
    {
        var translator = new FakeSentences(true, SentenceTranslation.Success("Привіт."));

        var result = await Call(translator, " Hello. ");

        Assert.Equal("Привіт.", Assert.IsType<Ok<SentenceTranslationResponse>>(result).Value!.Translation);
        Assert.Equal("pl", translator.LastTarget!.Code);
    }

    [Fact]
    public async Task Without_a_key_the_route_does_not_exist()
    {
        Assert.IsType<NotFound>(await Call(new FakeSentences(false, SentenceTranslation.Failure), "Hello."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Empty_text_is_a_bad_request(string? text)
    {
        Assert.IsType<BadRequest>(await Call(new FakeSentences(true, SentenceTranslation.Success("x")), text));
    }

    [Fact]
    public async Task Text_over_the_limit_is_a_bad_request()
    {
        var text = new string('a', TranslationEndpoints.MaxSentenceLength + 1);

        Assert.IsType<BadRequest>(await Call(new FakeSentences(true, SentenceTranslation.Success("x")), text));
    }

    [Fact]
    public async Task A_second_sentence_inside_the_window_is_429_with_the_wait_and_the_provider_is_not_asked()
    {
        var translator = new FakeSentences(true, SentenceTranslation.Success("x"));
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-29T12:00:00Z"));
        var limiter = new UncachedTranslationLimiter(clock);
        await Call(translator, "Hello.", limiter);

        clock.Advance(TimeSpan.FromSeconds(2.5));
        var http = new DefaultHttpContext();
        var result = await Call(translator, "Hello again.", limiter, http);

        Assert.Equal(429, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("8", http.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task A_bad_request_does_not_spend_the_slot()
    {
        var translator = new FakeSentences(true, SentenceTranslation.Success("Привіт."));
        var limiter = new UncachedTranslationLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.IsType<BadRequest>(await Call(translator, "   ", limiter));
        Assert.IsType<BadRequest>(await Call(translator, new string('a', TranslationEndpoints.MaxSentenceLength + 1), limiter));

        Assert.IsType<Ok<SentenceTranslationResponse>>(await Call(translator, "Hello.", limiter));
    }

    [Fact]
    public async Task Five_hundred_characters_are_accepted()
    {
        var result = await Call(
            new FakeSentences(true, SentenceTranslation.Success("x")), new string('a', 500));

        Assert.Equal(500, TranslationEndpoints.MaxSentenceLength);
        Assert.IsType<Ok<SentenceTranslationResponse>>(result);
    }

    [Fact]
    public async Task An_exhausted_provider_quota_is_503_quota()
    {
        var result = await Call(new FakeSentences(true, SentenceTranslation.Quota), "Hello.");

        var json = Assert.IsType<JsonHttpResult<SentenceTranslationError>>(result);
        Assert.Equal(503, json.StatusCode);
        Assert.Equal("quota", json.Value!.Reason);
    }

    [Fact]
    public async Task Any_other_provider_failure_is_502()
    {
        var result = await Call(new FakeSentences(true, SentenceTranslation.Failure), "Hello.");

        Assert.Equal(502, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_sentence_too_long_for_the_provider_is_413()
    {
        var result = await Call(new FakeSentences(true, SentenceTranslation.TooLong), "Hello.");

        Assert.Equal(413, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
    }
}
