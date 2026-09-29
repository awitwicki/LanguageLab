using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Application.Translation.Llm;

public static class WordTranslationServiceCollectionExtensions
{
    /// <summary>
    /// The LLM-backed word translation: the background queue's IWordBatchTranslator and the
    /// single-word ITranslator, both on top of ILlmClient (AddLlmClient). Plain AddScoped — unlike
    /// AddTranslationQueue's own IWordBatchTranslator default (NullWordBatchTranslator, registered
    /// with TryAddScoped), there is nothing here to win against, so this wins in either call order.
    /// </summary>
    public static IServiceCollection AddWordTranslation(this IServiceCollection services)
    {
        services.AddScoped<IWordBatchTranslator, LlmWordBatchTranslator>();
        services.AddScoped<ITranslator, LlmTranslator>();
        return services;
    }
}
