using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LanguageLab.Application.Translation.Llm;

/// <summary>
/// Says once, at startup, that translation is off because the selected provider has no key. The
/// app runs without one — in Production too — so the log is where an operator finds out.
/// </summary>
public sealed class LlmStartupCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<LlmStartupCheck> _logger;

    public LlmStartupCheck(IServiceScopeFactory scopes, IOptions<LlmOptions> options, ILogger<LlmStartupCheck> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // ILlmClient is scoped; a hosted service is a singleton, so it asks through a scope.
        using var scope = _scopes.CreateScope();

        if (!scope.ServiceProvider.GetRequiredService<ILlmClient>().IsConfigured)
        {
            _logger.LogWarning(
                "Translation provider {Provider} has no API key; translation is disabled", _options.Value.Provider);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
