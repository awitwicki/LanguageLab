using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Domain.Languages;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Tests;

public class LlmTranslatorTests
{
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;

    [Fact]
    public async Task Returns_the_batch_translators_answer_for_the_one_word_it_asked_about()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string> { ["house"] = "будинок" });
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());

        var result = await translator.TranslateAsync("house", Uk, CancellationToken.None);

        Assert.Equal("будинок", result);
        Assert.Equal(new[] { "house" }, batch.Batches.Single());
    }

    [Fact]
    public async Task A_word_the_batch_translator_has_no_answer_for_is_null()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string>());
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());

        var result = await translator.TranslateAsync("glorp", Uk, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task A_quota_exception_is_swallowed_to_null_and_logged_without_the_word()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string>());
        batch.Failures.Enqueue(new LlmQuotaException("quota", TimeSpan.FromSeconds(5)));
        var logger = new ListLogger<LlmTranslator>();
        var translator = new LlmTranslator(batch, logger);

        var result = await translator.TranslateAsync("house", Uk, CancellationToken.None);

        Assert.Null(result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain("house", entry.Message);
    }

    [Fact]
    public async Task An_unavailable_exception_is_swallowed_to_null()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string>());
        batch.Failures.Enqueue(new LlmUnavailableException("down"));
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());

        var result = await translator.TranslateAsync("house", Uk, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task A_word_over_100_characters_is_null_without_calling_the_batch_translator()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string>());
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());
        var word = new string('a', 101);

        var result = await translator.TranslateAsync(word, Uk, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(batch.Batches);
    }

    [Fact]
    public async Task A_word_of_exactly_100_characters_is_allowed_through()
    {
        var word = new string('a', 100);
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string> { [word] = "переклад" });
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());

        var result = await translator.TranslateAsync(word, Uk, CancellationToken.None);

        Assert.Equal("переклад", result);
    }

    [Fact]
    public async Task Cancellation_from_the_batch_translator_propagates_and_is_not_swallowed()
    {
        var batch = new FakeWordBatchTranslator(new Dictionary<string, string>());
        var translator = new LlmTranslator(batch, new ListLogger<LlmTranslator>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => translator.TranslateAsync("house", Uk, new CancellationToken(canceled: true)));
    }
}
