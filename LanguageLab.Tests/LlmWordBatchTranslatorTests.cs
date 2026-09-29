using System.Text.Json;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Domain.Languages;
using LanguageLab.Tests.Fakes;

namespace LanguageLab.Tests;

public class LlmWordBatchTranslatorTests
{
    private static readonly LearnerLanguage Polish = LearnerLanguages.Find("pl")!;

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement.Clone();

    [Fact]
    public async Task Sends_the_target_languages_name_the_distinct_lemmas_and_the_schema()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("""{"apple":"jabłko"}"""));
        var translator = new LlmWordBatchTranslator(client);

        await translator.TranslateAsync(["apple", "apple", "run"], Polish, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        Assert.Contains("Polish", request.SystemInstruction);
        Assert.Equal("""["apple","run"]""", request.UserContent);
        Assert.Equal(JsonValueKind.Object, request.ResponseSchema.ValueKind);
        Assert.Equal(
            "string",
            request.ResponseSchema.GetProperty("additionalProperties").GetProperty("type").GetString());
        Assert.True(request.MaxOutputTokens > 0);
    }

    [Fact]
    public async Task Uses_a_generous_output_token_ceiling_for_a_full_batch()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("{}"));
        var translator = new LlmWordBatchTranslator(client);

        await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        var request = Assert.Single(client.Requests);
        // ~200 lemmas (B1's batch size) in a non-Latin script leaves little room at 4000.
        Assert.True(request.MaxOutputTokens >= 8000);
    }

    [Fact]
    public async Task Returns_only_translations_for_requested_lemmas()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("""{"apple":"jabłko","glorp":"made up by the model"}"""));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal("apple", only.Key);
        Assert.Equal("jabłko", only.Value);
    }

    [Fact]
    public async Task A_key_that_differs_only_in_case_from_the_requested_lemma_still_matches()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("""{"Apple":"jabłko"}"""));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        Assert.Equal("jabłko", result["apple"]);
    }

    [Theory]
    [InlineData("""{"apple":""}""")]
    [InlineData("""{"apple":"   "}""")]
    [InlineData("""{"apple":42}""")]
    public async Task A_useless_value_is_dropped(string body)
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json(body));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>
    /// Many supported languages spell some words the same as English (German capitalises nouns:
    /// hand → Hand; Polish, Spanish, Italian and others keep problem/idea/hospital unchanged) — a
    /// value that merely echoes the lemma is not itself a sign of "no translation" the way it was
    /// for MyMemory, since the LLM is separately told to omit the key when it has nothing to say.
    /// </summary>
    [Theory]
    [InlineData("apple")]
    [InlineData("APPLE")]
    public async Task A_translation_that_spells_the_same_as_the_lemma_is_kept(string translation)
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json($$"""{"apple":"{{translation}}"}"""));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        Assert.Equal(translation, result["apple"]);
    }

    [Fact]
    public async Task Trims_surrounding_whitespace_from_the_translation()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("""{"apple":"  jabłko \n"}"""));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple"], Polish, CancellationToken.None);

        Assert.Equal("jabłko", result["apple"]);
    }

    [Fact]
    public async Task An_empty_object_answer_is_an_empty_result()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("{}"));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple", "run"], Polish, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Case_variant_duplicates_in_the_input_do_not_throw_and_the_first_wins()
    {
        var client = new FakeLlmClient();
        client.Answers.Enqueue(Json("""{"apple":"jabłko"}"""));
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync(["apple", "Apple"], Polish, CancellationToken.None);

        var only = Assert.Single(result);
        Assert.Equal("apple", only.Key);
    }

    [Fact]
    public void Is_configured_mirrors_the_client()
    {
        var client = new FakeLlmClient { IsConfigured = false };
        var translator = new LlmWordBatchTranslator(client);

        Assert.False(translator.IsConfigured);
    }

    [Fact]
    public async Task A_quota_exception_from_the_client_propagates_unchanged()
    {
        var client = new FakeLlmClient();
        client.Failures.Enqueue(new LlmQuotaException("quota", TimeSpan.FromSeconds(30)));
        var translator = new LlmWordBatchTranslator(client);

        var error = await Assert.ThrowsAsync<LlmQuotaException>(
            () => translator.TranslateAsync(["apple"], Polish, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task An_unavailable_exception_from_the_client_propagates_unchanged()
    {
        var client = new FakeLlmClient();
        client.Failures.Enqueue(new LlmUnavailableException("down"));
        var translator = new LlmWordBatchTranslator(client);

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => translator.TranslateAsync(["apple"], Polish, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_lemma_list_makes_no_call()
    {
        var client = new FakeLlmClient();
        var translator = new LlmWordBatchTranslator(client);

        var result = await translator.TranslateAsync([], Polish, CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(client.Requests);
    }
}
