using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Tests;

public class LlmServiceCollectionExtensionsTests
{
    private static ServiceProvider Services(params (string Key, string? Value)[] pairs)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

        return new ServiceCollection().AddLogging().AddLlmClient(configuration).BuildServiceProvider();
    }

    [Fact]
    public void Resolves_the_selector_as_the_scoped_llm_client()
    {
        using var services = Services(("Translation:Gemini:ApiKey", "gm-key"));
        using var scope = services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ILlmClient>();

        Assert.IsType<LlmClientSelector>(client);
        Assert.True(client.IsConfigured);
        Assert.Same(client, scope.ServiceProvider.GetRequiredService<ILlmClient>());
    }

    [Fact]
    public void Defaults_apply_when_the_section_is_empty()
    {
        using var services = Services();

        var options = services.GetRequiredService<IOptions<LlmOptions>>().Value;

        Assert.Equal(LlmProvider.Gemini, options.Provider);
        Assert.Null(options.Gemini.ApiKey);
        Assert.Equal("gemini-3.5-flash-lite", options.Gemini.Model);
        Assert.Equal("https://api.deepseek.com/", options.OpenAi.BaseUrl);
        Assert.Equal("deepseek-flash", options.OpenAi.Model);
    }

    [Fact]
    public void Binds_the_provider_models_and_keys_from_the_translation_section()
    {
        using var services = Services(
            ("Translation:Provider", "OpenAiCompatible"),
            ("Translation:Gemini:Model", "gemini-x"),
            ("Translation:OpenAi:ApiKey", "sk-key"),
            ("Translation:OpenAi:Model", "deepseek-pro"),
            ("Translation:OpenAi:BaseUrl", "https://llm.example.com/v1"));

        var options = services.GetRequiredService<IOptions<LlmOptions>>().Value;

        Assert.Equal(LlmProvider.OpenAiCompatible, options.Provider);
        Assert.Equal("gemini-x", options.Gemini.Model);
        Assert.Equal("sk-key", options.OpenAi.ApiKey);
        Assert.Equal("deepseek-pro", options.OpenAi.Model);
        Assert.Equal("https://llm.example.com/v1", options.OpenAi.BaseUrl);
        using var scope = services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<ILlmClient>().IsConfigured);
    }

    [Fact]
    public void Configures_both_http_clients()
    {
        using var services = Services(("Translation:OpenAi:BaseUrl", "https://llm.example.com/v1"));
        var factory = services.GetRequiredService<IHttpClientFactory>();

        var gemini = factory.CreateClient(nameof(GeminiLlmClient));
        var openAi = factory.CreateClient(nameof(OpenAiCompatibleLlmClient));

        Assert.Equal(new Uri("https://generativelanguage.googleapis.com/"), gemini.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(30), gemini.Timeout);
        Assert.Equal(new Uri("https://llm.example.com/v1/"), openAi.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(30), openAi.Timeout);
    }

    [Fact]
    public void An_unknown_provider_stops_startup()
    {
        using var services = Services(("Translation:Provider", "Claude"));

        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData("Translation:Provider", "7", "Translation:Provider must be Gemini or OpenAiCompatible.")]
    [InlineData("Translation:Gemini:Model", "", "Translation:Gemini:Model must not be blank.")]
    [InlineData("Translation:OpenAi:Model", " ", "Translation:OpenAi:Model must not be blank.")]
    [InlineData("Translation:OpenAi:BaseUrl", "not a url", "Translation:OpenAi:BaseUrl must be an absolute http(s) URL.")]
    [InlineData("Translation:OpenAi:BaseUrl", "ftp://llm.example.com/", "Translation:OpenAi:BaseUrl must be an absolute http(s) URL.")]
    public void An_invalid_setting_stops_startup(string key, string value, string expected)
    {
        using var services = Services((key, value));

        var error = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, error.Failures);
    }

    [Fact]
    public void The_startup_check_is_registered_as_a_hosted_service()
    {
        using var services = Services();

        Assert.Contains(services.GetServices<IHostedService>(), service => service is LlmStartupCheck);
    }

    [Theory]
    [InlineData("Gemini", "Translation:OpenAi:ApiKey")]
    [InlineData("OpenAiCompatible", "Translation:Gemini:ApiKey")]
    public async Task The_startup_check_warns_when_the_selected_provider_has_no_key(string provider, string otherKey)
    {
        using var services = Services(("Translation:Provider", provider), (otherKey, "some-key"));
        var log = new ListLogger<LlmStartupCheck>();
        var check = new LlmStartupCheck(
            services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<IOptions<LlmOptions>>(), log);

        await check.StartAsync(CancellationToken.None);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal($"Translation provider {provider} has no API key; translation is disabled", entry.Message);
    }

    [Fact]
    public async Task The_startup_check_is_silent_with_a_key()
    {
        using var services = Services(("Translation:Gemini:ApiKey", "gm-key"));
        var log = new ListLogger<LlmStartupCheck>();
        var check = new LlmStartupCheck(
            services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<IOptions<LlmOptions>>(), log);

        await check.StartAsync(CancellationToken.None);

        Assert.Empty(log.Entries);
    }
}
