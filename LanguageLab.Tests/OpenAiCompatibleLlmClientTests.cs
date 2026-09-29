using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class OpenAiCompatibleLlmClientTests
{
    // Neither sentinel may reach an exception message or a log line.
    private const string UserText = "SECRET-USER-TEXT Call me Ishmael.";
    private const string EchoedBody = "SECRET-BODY-ECHO";
    private const string Key = "sk-test-key";
    private const string SchemaText = """{"type":"object","properties":{"word":{"type":"string"}},"required":["word"]}""";

    private static readonly JsonElement Schema = JsonDocument.Parse(SchemaText).RootElement;

    private static LlmRequest Request() => new("Translate the word.", UserText, Schema, 256);

    private static (OpenAiCompatibleLlmClient Client, ListLogger<OpenAiCompatibleLlmClient> Log) Create(
        StubHttpHandler handler, string? key = Key, string baseUrl = "https://api.deepseek.com/")
    {
        var log = new ListLogger<OpenAiCompatibleLlmClient>();
        var client = new OpenAiCompatibleLlmClient(
            new HttpClient(handler) { BaseAddress = OpenAiCompatibleLlmClient.BaseAddressFor(baseUrl) },
            Options.Create(new LlmOptions
            {
                Provider = LlmProvider.OpenAiCompatible,
                OpenAi = new OpenAiCompatibleOptions { ApiKey = key, BaseUrl = baseUrl },
            }),
            log);
        return (client, log);
    }

    private static string Answer(string content, string finishReason = "stop") =>
        JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = finishReason } },
        });

    private static string ErrorBody(int status) =>
        JsonSerializer.Serialize(new { error = new { code = status, message = $"{EchoedBody} {UserText}" } });

    private static void AssertNothingLeaked(Exception error, ListLogger<OpenAiCompatibleLlmClient> log)
    {
        foreach (var text in log.Entries.Select(entry => entry.Message).Append(error.Message))
        {
            Assert.DoesNotContain("SECRET", text);
            Assert.DoesNotContain(Key, text);
        }
    }

    [Fact]
    public async Task Sends_a_bearer_key_to_chat_completions()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        var sent = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.deepseek.com/chat/completions", sent.RequestUri!.ToString());
        Assert.Equal($"Bearer {Key}", sent.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task A_key_with_surrounding_whitespace_is_trimmed_before_it_is_sent()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler, key: $" {Key}\r\n").Client.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal($"Bearer {Key}", handler.LastRequest!.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task A_base_url_with_a_path_keeps_it()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler, baseUrl: "https://llm.example.com/v1").Client.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal("https://llm.example.com/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/")]
    [InlineData("https://llm.example.com/v1", "https://llm.example.com/v1/")]
    [InlineData("https://llm.example.com/v1/", "https://llm.example.com/v1/")]
    public void Base_address_always_ends_in_a_slash(string baseUrl, string expected)
    {
        Assert.Equal(new Uri(expected), OpenAiCompatibleLlmClient.BaseAddressFor(baseUrl));
    }

    [Fact]
    public async Task Body_carries_the_model_json_mode_the_token_cap_and_zero_temperature()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("deepseek-flash", root.GetProperty("model").GetString());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0d, root.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task The_system_message_holds_the_instruction_and_the_schema_and_the_user_message_the_content()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        // "JSON" in the prompt is what DeepSeek's JSON mode requires.
        Assert.Equal(
            $"Translate the word.\n\nAnswer with a single JSON object that matches this JSON Schema:\n{SchemaText}",
            messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal(UserText, messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Returns_an_object_that_stays_readable_after_the_call()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        var answer = await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal(JsonValueKind.Object, answer.ValueKind);
        Assert.Equal("кит", answer.GetProperty("word").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Without_a_key_nothing_is_sent(string? key)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("{}")));
        var (client, log) = Create(handler, key);

        Assert.False(client.IsConfigured);
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));
        Assert.Equal("OpenAiCompatible is not configured.", error.Message);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData(429, 12.0)]
    [InlineData(402, null)]
    public async Task Status_429_and_402_are_quota(int status, double? expectedSeconds)
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = StubHttpHandler.Json(ErrorBody(status), (HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return response;
        });
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmQuotaException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal($"OpenAiCompatible refused the call for quota ({status}).", error.Message);
        Assert.Equal<TimeSpan?>(expectedSeconds.HasValue ? TimeSpan.FromSeconds(expectedSeconds.Value) : null, error.RetryAfter);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Any_other_status_is_unavailable(int status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(ErrorBody(status), (HttpStatusCode)status));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal($"OpenAiCompatible answered {status}.", error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    /// <summary>OpenAI-style error.code is a short slug, unlike Gemini's numeric one — worth keeping.</summary>
    [Fact]
    public async Task A_failing_status_names_the_providers_error_code_when_present()
    {
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = "invalid_api_key", message = $"{EchoedBody} {UserText}" },
        });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body, HttpStatusCode.BadRequest));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("OpenAiCompatible answered 400 (invalid_api_key).", error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    /// <summary>error.type is the fallback when code is absent (or, as here, null).</summary>
    [Fact]
    public async Task Falls_back_to_error_type_when_code_is_absent()
    {
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = (string?)null, type = "invalid_request_error", message = $"{EchoedBody} {UserText}" },
        });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body, HttpStatusCode.BadRequest));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("OpenAiCompatible answered 400 (invalid_request_error).", error.Message);
        AssertNothingLeaked(error, log);
    }

    public static TheoryData<string, string> UnusableAnswers => new()
    {
        { """{"choices":[]}""", "OpenAiCompatible answered without a choice." },
        { Answer("""{"word":"к""", "length"), "OpenAiCompatible finish reason length." },
        { Answer("{}", "content_filter"), "OpenAiCompatible finish reason content_filter." },
        { """{"choices":[{"message":{"role":"assistant","content":"{}"}}]}""", "OpenAiCompatible finish reason missing." },
        { Answer("{}", EchoedBody + " in a finish reason"), "OpenAiCompatible finish reason unrecognised." },
        { """{"choices":[{"message":{"role":"assistant","content":null},"finish_reason":"stop"}]}""", "OpenAiCompatible answered with empty text." },
        { Answer("the whale " + EchoedBody), "OpenAiCompatible answered text that is not JSON." },
        { Answer("""["кит"]"""), "OpenAiCompatible answered JSON that is not an object." },
        { "<html>" + EchoedBody + "</html>", "OpenAiCompatible answered a body that is not JSON." },
    };

    [Theory]
    [MemberData(nameof(UnusableAnswers))]
    public async Task Unusable_answers_are_unavailable(string body, string expected)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal(expected, error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_and_keeps_the_cause()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException($"connection reset while sending {UserText}"));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("OpenAiCompatible request failed: HttpRequestException.", error.Message);
        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    [Fact]
    public async Task A_timeout_is_unavailable_not_a_cancellation()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException("timed out"));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("OpenAiCompatible request failed: TaskCanceledException.", error.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        AssertNothingLeaked(error, log);
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_anything_is_sent()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("{}")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(handler).Client.CompleteJsonAsync(Request(), new CancellationToken(canceled: true)));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_cancellation_during_the_call_propagates_unwrapped()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHttpHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var (client, log) = Create(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CompleteJsonAsync(Request(), cancellation.Token));

        Assert.Empty(log.Entries);
    }
}
