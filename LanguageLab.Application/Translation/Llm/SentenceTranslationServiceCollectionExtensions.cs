using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Application.Translation.Llm;

public static class SentenceTranslationServiceCollectionExtensions
{
    /// <summary>The reader's sentence translation, on top of ILlmClient (AddLlmClient).</summary>
    public static IServiceCollection AddSentenceTranslation(this IServiceCollection services)
    {
        services.AddScoped<ISentenceTranslator, LlmSentenceTranslator>();
        return services;
    }
}
