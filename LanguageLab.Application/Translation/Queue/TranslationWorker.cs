using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LanguageLab.Application.Translation.Queue;

/// <summary>What the worker does after an iteration: go on at once (a zero Delay), or wait —
/// cut short by an enqueue when <see cref="WakesOnSignal"/>.</summary>
public readonly record struct WorkerPause(TimeSpan Delay, bool WakesOnSignal);

/// <summary>
/// Fills dictionaries' missing translations in the background: one TranslationJobProcessor
/// batch per iteration, each in its own DI scope, then a pause chosen by <see cref="PauseAfter"/>.
/// Never takes the host down — every failure is caught, logged by type, and waited out.
/// </summary>
public sealed class TranslationWorker : BackgroundService
{
    /// <summary>No pending job: an enqueue wakes the worker sooner.</summary>
    public static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(30);

    /// <summary>No translator configured: nothing can happen until one is, so check rarely.</summary>
    public static readonly TimeSpan UnconfiguredWait = TimeSpan.FromMinutes(5);

    /// <summary>After an unavailable batch or an unexpected exception.</summary>
    public static readonly TimeSpan FailureWait = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TranslationQueueSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<TranslationWorker> _logger;

    public TranslationWorker(
        IServiceScopeFactory scopes,
        TranslationQueueSignal signal,
        TimeProvider time,
        ILogger<TranslationWorker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Work done goes on at once with the next job in turn. A failed batch waits a minute, so a
    /// short outage cannot spend a job's five attempts in a second. A quota refusal backs off for
    /// the whole worker (the quota is shared) and ignores enqueues. Idle waits for an enqueue.
    /// </summary>
    public static WorkerPause PauseAfter(ProcessOutcome outcome, bool translatorConfigured, TimeSpan? previousQuotaWait) =>
        outcome.Kind switch
        {
            ProcessOutcomeKind.Progressed or ProcessOutcomeKind.Completed => new WorkerPause(TimeSpan.Zero, false),
            ProcessOutcomeKind.Failed => new WorkerPause(FailureWait, false),
            ProcessOutcomeKind.QuotaExceeded =>
                new WorkerPause(TranslationBackoff.Next(outcome.RetryAfter, previousQuotaWait), false),
            ProcessOutcomeKind.Idle => translatorConfigured
                ? new WorkerPause(IdleWait, true)
                : new WorkerPause(UnconfiguredWait, true),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, null),
        };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan? quotaWait = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WorkerPause pause;

                await using (var scope = _scopes.CreateAsyncScope())
                {
                    var outcome = await scope.ServiceProvider
                        .GetRequiredService<TranslationJobProcessor>()
                        .ProcessNextAsync(stoppingToken);
                    var configured = scope.ServiceProvider.GetRequiredService<IWordBatchTranslator>().IsConfigured;

                    pause = PauseAfter(outcome, configured, quotaWait);

                    if (outcome.Kind == ProcessOutcomeKind.QuotaExceeded)
                    {
                        quotaWait = pause.Delay;
                        _logger.LogWarning(
                            "Translation paused for {Wait} after a quota refusal (provider hint {RetryAfter})",
                            pause.Delay, outcome.RetryAfter);
                    }
                    else
                    {
                        quotaWait = null;
                    }
                }

                await PauseAsync(pause, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // By type only: a message can carry a provider's error body, which can echo words.
                _logger.LogError(
                    "Translation worker iteration failed with {ExceptionType}; retrying in {Wait}",
                    e.GetType().Name, FailureWait);

                try
                {
                    await Task.Delay(FailureWait, _time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task PauseAsync(WorkerPause pause, CancellationToken stoppingToken)
    {
        if (pause.Delay <= TimeSpan.Zero)
        {
            return;
        }

        if (pause.WakesOnSignal)
        {
            await _signal.WaitAsync(pause.Delay, stoppingToken);
        }
        else
        {
            await Task.Delay(pause.Delay, _time, stoppingToken);
        }
    }
}
