using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Queue;
using LanguageLab.Domain.Languages;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanguageLab.Tests;

public class TranslationQueueRegistrationTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services;
    }

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    private static FakeWordBatchTranslator Real() => new(new Dictionary<string, string>());

    [Fact]
    public async Task Registers_the_queue_the_processor_the_worker_and_the_defaults()
    {
        var services = Services();
        services.AddTranslationQueue();
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<TranslationQueue>(scope.ServiceProvider.GetRequiredService<ITranslationQueue>());
        Assert.IsType<NullWordBatchTranslator>(scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TranslationJobProcessor>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        Assert.Same(provider.GetRequiredService<TranslationQueueSignal>(), provider.GetRequiredService<TranslationQueueSignal>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is TranslationWorker);
    }

    [Fact]
    public async Task A_translator_registered_before_wins_over_the_null_one()
    {
        var real = Real();
        var services = Services();
        services.AddScoped<IWordBatchTranslator>(_ => real);
        services.AddTranslationQueue();
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.Same(real, scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
    }

    [Fact]
    public async Task A_translator_registered_after_wins_over_the_null_one()
    {
        var real = Real();
        var services = Services();
        services.AddTranslationQueue();
        services.AddScoped<IWordBatchTranslator>(_ => real);
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.Same(real, scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
    }

    [Fact]
    public async Task The_null_translator_is_unconfigured_and_unavailable()
    {
        var translator = new NullWordBatchTranslator();

        Assert.False(translator.IsConfigured);
        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => translator.TranslateAsync(["house"], LearnerLanguages.Default, CancellationToken.None));
    }
}
