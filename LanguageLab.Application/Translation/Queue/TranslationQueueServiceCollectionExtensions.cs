using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LanguageLab.Application.Translation.Queue;

public static class TranslationQueueServiceCollectionExtensions
{
    /// <summary>
    /// The background dictionary translation: the queue callers enqueue through, the processor
    /// and the hosted worker. TimeProvider and IWordBatchTranslator are TryAdd'ed — the system
    /// clock and the never-configured NullWordBatchTranslator are only defaults, and a real
    /// translator registered in either order wins.
    /// </summary>
    public static IServiceCollection AddTranslationQueue(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TranslationQueueSignal>();
        services.AddScoped<ITranslationQueue, TranslationQueue>();
        services.AddScoped<TranslationJobProgressReader>();
        services.AddScoped<TranslationJobProcessor>();
        services.TryAddScoped<IWordBatchTranslator, NullWordBatchTranslator>();
        services.AddHostedService<TranslationWorker>();
        return services;
    }
}
