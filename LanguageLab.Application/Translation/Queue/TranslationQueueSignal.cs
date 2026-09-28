namespace LanguageLab.Application.Translation.Queue;

/// <summary>
/// Wakes the idle translation worker when a job is enqueued, so a new dictionary does not wait
/// out the idle timeout. Wakes coalesce: however many arrive while the worker is busy, it sees
/// one. A singleton, shared by every request's TranslationQueue and the worker.
/// </summary>
public sealed class TranslationQueueSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Wake()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already woken and not yet consumed — one wake is all the worker needs.
        }
    }

    /// <summary>True when woken, false when <paramref name="timeout"/> ran out first.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(timeout, cancellationToken);
}
