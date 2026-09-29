using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class GeminiLlmClientTests
{
    // Neither sentinel may reach an exception message or a log line: the first stands for a
    // sentence of someone's book, the second for a provider error body that echoes it back.
    private const string UserText = "SECRET-USER-TEXT Call me Ishmael.";
    private const string EchoedBody = "SECRET-BODY-ECHO";
    private const string Key = "gm-test-key";

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"word":{"type":"string"}},"required":["word"]}""").RootElement;

    private static LlmRequest Request() => new("Translate the word.", UserText, Schema, 256);

    private static (GeminiLlmClient Client, ListLogger<GeminiLlmClient> Log) Create(StubHttpHandler handler, string? key = Key)
    {
        var log = new ListLogger<GeminiLlmClient>();
        var client = new GeminiLlmClient(
            new HttpClient(handler) { BaseAddress = new Uri(GeminiLlmClient.BaseUrl) },
            Options.Create(new LlmOptions { Gemini = new GeminiOptions { ApiKey = key } }),
            log);
        return (client, log);
    }

    private static string Answer(string text, string finishReason = "STOP") =>
        JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { role = "model", parts = new[] { new { text } } }, finishReason } },
        });

    private static string ErrorBody(int status) =>
        JsonSerializer.Serialize(new { error = new { code = status, message = $"{EchoedBody} {UserText}" } });

    private static void AssertNothingLeaked(Exception error, ListLogger<GeminiLlmClient> log)
    {
        foreach (var text in log.Entries.Select(entry => entry.Message).Append(error.Message))
        {
            Assert.DoesNotContain("SECRET", text);
            Assert.DoesNotContain(Key, text);
        }
    }

    [Fact]
    public async Task Sends_the_key_in_a_header_and_the_model_in_the_path()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        var sent = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("/v1beta/models/gemini-3.5-flash-lite:generateContent", sent.RequestUri!.AbsolutePath);
        Assert.Empty(sent.RequestUri.Query);
        Assert.DoesNotContain(Key, sent.RequestUri.ToString());
        Assert.Equal(Key, sent.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task A_key_with_surrounding_whitespace_is_trimmed_before_it_is_sent()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler, key: $" {Key}\r\n").Client.CompleteJsonAsync(Request(), CancellationToken.None);

        Assert.Equal(Key, handler.LastRequest!.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Body_carries_the_instruction_the_content_and_the_generation_config()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("Translate the word.", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var content = Assert.Single(root.GetProperty("contents").EnumerateArray());
        Assert.Equal("user", content.GetProperty("role").GetString());
        Assert.Equal(UserText, content.GetProperty("parts")[0].GetProperty("text").GetString());
        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.True(JsonElement.DeepEquals(Schema, config.GetProperty("responseJsonSchema")));
        Assert.Equal(256, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0d, config.GetProperty("temperature").GetDouble());
        Assert.Equal("minimal", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Fact]
    public async Task Returns_an_object_that_stays_readable_after_the_call()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(Answer("""{"word":"кит"}""")));

        var answer = await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

        // A JsonElement of a disposed JsonDocument throws here; the client must return a clone.
        Assert.Equal(JsonValueKind.Object, answer.ValueKind);
        Assert.Equal("кит", answer.GetProperty("word").GetString());
    }

    [Fact]
    public async Task Joins_answer_parts_and_skips_thoughts()
    {
        const string body = """
            {"candidates":[{"content":{"role":"model","parts":[
              {"text":"Let me think about whales.","thought":true},
              {"text":"{\"word\":"},
              {"text":"\"кит\"}","thoughtSignature":"c2lnbmF0dXJl"}
            ]},"finishReason":"STOP"}]}
            """;
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body));

        var answer = await Create(handler).Client.CompleteJsonAsync(Request(), CancellationToken.None);

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
        Assert.Equal("Gemini is not configured.", error.Message);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Any_other_status_is_unavailable(int status)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(ErrorBody(status), (HttpStatusCode)status));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal($"Gemini answered {status}.", error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    /// <summary>
    /// error.status is Google's own machine-readable code — distinct from the classic bad-API-key
    /// case, but any 400 caused by a bad or wrong-project key surfaces this way too, and right now
    /// that reason is thrown away along with the (rightly never-logged) message.
    /// </summary>
    [Fact]
    public async Task A_failing_status_names_googles_error_code_when_present()
    {
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = 400, status = "INVALID_ARGUMENT", message = $"{EchoedBody} {UserText}" },
        });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body, HttpStatusCode.BadRequest));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("Gemini answered 400 (INVALID_ARGUMENT).", error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    /// <summary>A status that isn't a clean code (defense in depth, like ReasonCode elsewhere) never quotes it raw.</summary>
    [Fact]
    public async Task An_unrecognised_error_status_is_never_quoted_raw()
    {
        var body = JsonSerializer.Serialize(new { error = new { code = 400, status = $"{EchoedBody} {UserText}" } });
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(body, HttpStatusCode.BadRequest));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("Gemini answered 400 (unrecognised).", error.Message);
        AssertNothingLeaked(error, log);
    }

    public static TheoryData<string, string> UnusableAnswers => new()
    {
        { """{"promptFeedback":{"blockReason":"SAFETY"}}""", "Gemini blocked the prompt: SAFETY." },
        { """{"candidates":[]}""", "Gemini answered without a candidate." },
        { Answer("""{"word":"к""", "MAX_TOKENS"), "Gemini finish reason MAX_TOKENS." },
        { """{"candidates":[{"content":{"parts":[{"text":"{}"}]}}]}""", "Gemini finish reason missing." },
        { Answer("{}", EchoedBody + " in a finish reason"), "Gemini finish reason unrecognised." },
        { Answer("  "), "Gemini answered with empty text." },
        { Answer("the whale " + EchoedBody), "Gemini answered text that is not JSON." },
        { Answer("""["кит"]"""), "Gemini answered JSON that is not an object." },
        { "<html>" + EchoedBody + "</html>", "Gemini answered a body that is not JSON." },
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

        Assert.Equal("Gemini request failed: HttpRequestException.", error.Message);
        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
    }

    [Fact]
    public async Task A_timeout_is_unavailable_not_a_cancellation()
    {
        // HttpClient reports its own timeout as TaskCanceledException while the caller's token is live.
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException("timed out"));
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("Gemini request failed: TaskCanceledException.", error.Message);
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

    private static string QuotaBody(string retryDelay) =>
        JsonSerializer.Serialize(new
        {
            error = new
            {
                code = 429,
                message = $"{EchoedBody} Resource has been exhausted",
                status = "RESOURCE_EXHAUSTED",
                details = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "type.googleapis.com/google.rpc.QuotaFailure",
                        ["violations"] = Array.Empty<object>(),
                    },
                    new Dictionary<string, object>
                    {
                        ["@type"] = "type.googleapis.com/google.rpc.RetryInfo",
                        ["retryDelay"] = retryDelay,
                    },
                },
            },
        });

    private static StubHttpHandler QuotaAnswer(string body, RetryConditionHeaderValue? retryAfter = null) =>
        new(_ =>
        {
            var response = StubHttpHandler.Json(body, HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = retryAfter;
            return response;
        });

    private static async Task<LlmQuotaException> QuotaAsync(StubHttpHandler handler)
    {
        var (client, log) = Create(handler);

        var error = await Assert.ThrowsAsync<LlmQuotaException>(
            () => client.CompleteJsonAsync(Request(), CancellationToken.None));

        Assert.Equal("Gemini refused the call for quota (429).", error.Message);
        Assert.Equal(error.Message, Assert.Single(log.Entries).Message);
        AssertNothingLeaked(error, log);
        return error;
    }

    [Fact]
    public async Task Quota_takes_the_Retry_After_seconds_before_the_body()
    {
        var error = await QuotaAsync(QuotaAnswer(QuotaBody("37s"), new RetryConditionHeaderValue(TimeSpan.FromSeconds(12))));

        Assert.Equal(TimeSpan.FromSeconds(12), error.RetryAfter);
    }

    [Fact]
    public async Task Quota_takes_a_Retry_After_date_as_a_wait_from_now()
    {
        var error = await QuotaAsync(QuotaAnswer("{}", new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2))));

        Assert.InRange(error.RetryAfter!.Value, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(120));
    }

    [Fact]
    public async Task Quota_with_a_Retry_After_date_in_the_past_waits_zero()
    {
        var error = await QuotaAsync(QuotaAnswer("{}", new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5))));

        Assert.Equal(TimeSpan.Zero, error.RetryAfter);
    }

    [Theory]
    [InlineData("37s", 37_000)]
    [InlineData("1.5s", 1_500)]
    public async Task Quota_falls_back_to_the_bodys_retry_delay(string retryDelay, int milliseconds)
    {
        var error = await QuotaAsync(QuotaAnswer(QuotaBody(retryDelay)));

        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), error.RetryAfter);
    }

    public static TheoryData<string> BodiesWithoutAHint => new()
    {
        "{}",
        "not json",
        "",
        QuotaBody("soon"),
        QuotaBody("-5s"),
    };

    [Theory]
    [MemberData(nameof(BodiesWithoutAHint))]
    public async Task Quota_without_a_usable_hint_has_no_retry_after(string body)
    {
        var error = await QuotaAsync(QuotaAnswer(body));

        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task Quota_clamps_an_absurd_retry_delay_to_a_day()
    {
        var error = await QuotaAsync(QuotaAnswer(QuotaBody("99999999999999999999s")));

        Assert.Equal(TimeSpan.FromDays(1), error.RetryAfter);
    }
}
