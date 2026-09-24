using LanguageLab.Api;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Translation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LanguageLab.Tests;

public class TranslateSentenceEndpointTests
{
    private sealed class FakeUser : ICurrentUser
    {
        public Task<long> GetIdAsync() => Task.FromResult(7L);
    }

    private sealed class FakeSentences(bool configured, SentenceTranslation answer) : ISentenceTranslator
    {
        public int Calls { get; private set; }

        public bool IsConfigured => configured;

        public Task<SentenceTranslation> TranslateAsync(string text, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private static Task<IResult> Call(ISentenceTranslator translator, string? text, SentenceQuota? quota = null) =>
        TranslationEndpoints.TranslateSentenceAsync(
            new SentenceRequest(text), translator, quota ?? new SentenceQuota(), new FakeUser(), CancellationToken.None);

    [Fact]
    public async Task A_translation_comes_back()
    {
        var result = await Call(new FakeSentences(true, SentenceTranslation.Success("Привіт.")), " Hello. ");

        Assert.Equal("Привіт.", Assert.IsType<Ok<SentenceTranslationResponse>>(result).Value!.Translation);
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
    public async Task A_spent_day_is_429_and_the_provider_is_not_asked()
    {
        var translator = new FakeSentences(true, SentenceTranslation.Success("x"));

        var result = await Call(translator, "Hello there.", new SentenceQuota(characters: 5, window: TimeSpan.FromDays(1)));

        Assert.Equal(429, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, translator.Calls);
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
}
