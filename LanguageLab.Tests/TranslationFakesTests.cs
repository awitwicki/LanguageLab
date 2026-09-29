using System.Text.Json;
using LanguageLab.Application.Translation;
using LanguageLab.Domain.Languages;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests;

public class TranslationFakesTests
{
    private static readonly LearnerLanguage Uk = LearnerLanguages.Default;

    private static LlmRequest Request() =>
        new("system", "{}", JsonDocument.Parse("""{"type":"object"}""").RootElement, 256);

    [Fact]
    public async Task Llm_client_answers_with_the_queued_json_and_records_the_request()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(JsonDocument.Parse("""{"ok":true}""").RootElement);

        var answer = await client.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task Llm_client_throws_the_queued_exception()
    {
        var client = new FakeLlmClient();
        client.Failures.Enqueue(new LlmQuotaException("quota", TimeSpan.FromMinutes(1)));

        var error = await Assert.ThrowsAsync<LlmQuotaException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromMinutes(1), error.RetryAfter);
    }

    [Fact]
    public async Task Unconfigured_llm_client_is_unavailable()
    {
        var client = new FakeLlmClient { IsConfigured = false };

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Llm_client_honours_cancellation()
    {
        var client = new FakeLlmClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteJsonAsync(Request(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Word_batch_translator_answers_only_for_requested_lemmas_it_knows()
    {
        var translator = new FakeWordBatchTranslator(new Dictionary<string, string>
        {
            ["house"] = "будинок",
            ["tree"] = "дерево",
        });

        var result = await translator.TranslateAsync(["house", "glorp"], Uk, CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal("house", only.Key);
        Assert.Equal("будинок", only.Value);
        Assert.Equal(new[] { "house", "glorp" }, translator.Batches.Single());
    }

    [Fact]
    public async Task Word_batch_translator_throws_the_queued_exception_and_honours_configuration()
    {
        var translator = new FakeWordBatchTranslator(new Dictionary<string, string>());
        translator.Failures.Enqueue(new LlmQuotaException("quota", null));

        await Assert.ThrowsAsync<LlmQuotaException>(
            () => translator.TranslateAsync(["house"], Uk, CancellationToken.None));

        translator.IsConfigured = false;
        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => translator.TranslateAsync(["house"], Uk, CancellationToken.None));
    }

    [Fact]
    public async Task Word_batch_translator_honours_cancellation()
    {
        var translator = new FakeWordBatchTranslator(new Dictionary<string, string>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => translator.TranslateAsync(["house"], Uk, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Translation_queue_records_every_call_in_order()
    {
        var queue = new FakeTranslationQueue();

        await queue.EnqueueAsync(7L, Uk, CancellationToken.None);
        await queue.EnqueueAsync(7L, Uk, CancellationToken.None);

        Assert.Equal(new[] { (7L, "uk"), (7L, "uk") }, queue.Enqueued);
    }

    [Fact]
    public async Task Word_batch_translator_runs_the_hook_before_answering()
    {
        var translator = new FakeWordBatchTranslator(new Dictionary<string, string> { ["house"] = "будинок" });
        IReadOnlyList<string>? seen = null;
        translator.BeforeAnswer = lemmas =>
        {
            seen = lemmas;
            return Task.CompletedTask;
        };

        var result = await translator.TranslateAsync(["house"], Uk, CancellationToken.None);

        Assert.Equal(new[] { "house" }, seen);
        Assert.Equal("будинок", result["house"]);
    }
}
