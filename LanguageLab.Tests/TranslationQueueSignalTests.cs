using LanguageLab.Application.Translation.Queue;

namespace LanguageLab.Tests;

public class TranslationQueueSignalTests
{
    [Fact]
    public async Task A_wake_before_the_wait_returns_at_once()
    {
        var signal = new TranslationQueueSignal();

        signal.Wake();
        var wait = signal.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.True(wait.IsCompleted);
        Assert.True(await wait);
    }

    [Fact]
    public async Task A_wait_without_a_wake_times_out()
    {
        var signal = new TranslationQueueSignal();

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task Wakes_coalesce_into_one()
    {
        var signal = new TranslationQueueSignal();

        signal.Wake();
        signal.Wake();

        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task A_wait_honours_cancellation()
    {
        var signal = new TranslationQueueSignal();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signal.WaitAsync(TimeSpan.FromMinutes(1), new CancellationToken(canceled: true)));
    }
}
