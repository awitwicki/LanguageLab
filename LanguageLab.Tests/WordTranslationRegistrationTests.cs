using LanguageLab.Application.Translation;
using LanguageLab.Application.Translation.Llm;
using LanguageLab.Application.Translation.Queue;
using LanguageLab.Infrastructure.Database;
using LanguageLab.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LanguageLab.Tests;

public class WordTranslationRegistrationTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<ILlmClient>(new FakeLlmClient());
        return services;
    }

    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    [Fact]
    public async Task Registers_the_batch_translator_and_the_single_word_translator()
    {
        var services = Services();
        services.AddWordTranslation();
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<LlmWordBatchTranslator>(scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
        Assert.IsType<LlmTranslator>(scope.ServiceProvider.GetRequiredService<ITranslator>());
    }

    [Fact]
    public async Task Wins_over_the_queues_null_default_when_added_before()
    {
        var services = Services();
        services.AddWordTranslation();
        services.AddTranslationQueue();
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<LlmWordBatchTranslator>(scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
    }

    [Fact]
    public async Task Wins_over_the_queues_null_default_when_added_after()
    {
        var services = Services();
        services.AddTranslationQueue();
        services.AddWordTranslation();
        await using var provider = Build(services);
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<LlmWordBatchTranslator>(scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>());
    }
}
