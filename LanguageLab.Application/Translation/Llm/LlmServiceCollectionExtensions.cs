using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="LlmOptions"/> from the "Translation" section and validates it at startup
    /// (an unknown provider, a blank model or a bad base URL is a deploy mistake: the app does not
    /// start), registers both typed clients, <see cref="LlmClientSelector"/> as the scoped
    /// <see cref="ILlmClient"/>, and <see cref="LlmStartupCheck"/>. A missing key is not an error.
    /// </summary>
    public static IServiceCollection AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .Validate(options => Enum.IsDefined(options.Provider),
                "Translation:Provider must be Gemini or OpenAiCompatible.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Gemini.Model),
                "Translation:Gemini:Model must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.OpenAi.Model),
                "Translation:OpenAi:Model must not be blank.")
            .Validate(options => IsHttpUrl(options.OpenAi.BaseUrl),
                "Translation:OpenAi:BaseUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        services.AddHttpClient<GeminiLlmClient>(client =>
        {
            client.BaseAddress = new Uri(GeminiLlmClient.BaseUrl);
            client.Timeout = LlmHttp.Timeout;
        });
        services.AddHttpClient<OpenAiCompatibleLlmClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
            client.BaseAddress = OpenAiCompatibleLlmClient.BaseAddressFor(options.OpenAi.BaseUrl);
            client.Timeout = LlmHttp.Timeout;
        });

        services.AddScoped<ILlmClient, LlmClientSelector>();
        services.AddHostedService<LlmStartupCheck>();

        return services;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
