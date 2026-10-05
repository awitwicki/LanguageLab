using LanguageLab.Api;
using LanguageLab.Api.Endpoints;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LanguageLab.Tests;

public class TranslateWordEndpointTests
{
    private sealed class FakeUser : ICurrentUser
    {
        public Task<long> GetIdAsync() => Task.FromResult(7L);
    }

    private sealed class FakeLanguage : ICurrentLanguage
    {
        public LearnerLanguage? Get() => LearnerLanguages.Default;
    }

    private sealed class FakeTranslator : ITranslator
    {
        public int Calls { get; private set; }

        public Task<string?> TranslateAsync(string word, LearnerLanguage target, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<string?>("переклад");
        }
    }

    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Task<IResult> Call(TranslationService translation, string word, HttpContext? http = null) =>
        TranslationEndpoints.TranslateWordAsync(
            word, translation, new FakeUser(), new FakeLanguage(), http ?? new DefaultHttpContext(), CancellationToken.None);

    [Fact]
    public async Task A_second_miss_inside_the_window_is_429_with_the_exact_wait()
    {
        await using var db = NewContext();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-29T12:00:00Z"));
        var translator = new FakeTranslator();
        var translation = new TranslationService(db, translator, new UncachedTranslationLimiter(clock), FakeEnglishLexicon.Knowing("orphan"));

        var first = await Call(translation, "orphan");
        clock.Advance(TimeSpan.FromSeconds(1));
        var http = new DefaultHttpContext();
        var second = await Call(translation, "waif", http);

        Assert.Equal("переклад", Assert.IsType<Ok<TranslationLookup>>(first).Value!.Translation);
        Assert.Equal(429, Assert.IsType<StatusCodeHttpResult>(second).StatusCode);
        Assert.Equal("9", http.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, translator.Calls);
    }

    [Fact]
    public async Task A_cache_hit_right_after_a_miss_is_answered()
    {
        await using var db = NewContext();
        var translation = new TranslationService(
            db, new FakeTranslator(), new UncachedTranslationLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch)),
            FakeEnglishLexicon.Knowing("orphan"));

        await Call(translation, "orphan");
        var again = await Call(translation, "orphan");

        Assert.Equal(TranslationSource.Dictionary, Assert.IsType<Ok<TranslationLookup>>(again).Value!.Source);
    }

    [Fact]
    public async Task An_invalid_word_is_a_bad_request()
    {
        await using var db = NewContext();
        var translation = new TranslationService(
            db, new FakeTranslator(), new UncachedTranslationLimiter(new ManualTimeProvider(DateTimeOffset.UnixEpoch)),
            FakeEnglishLexicon.Knowing("orphan"));

        Assert.IsType<BadRequest>(await Call(translation, "   "));
    }
}
