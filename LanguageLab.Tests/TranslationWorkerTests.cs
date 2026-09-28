using LanguageLab.Application.Translation.Queue;

namespace LanguageLab.Tests;

/// <summary>The loop itself is not unit-tested; what it does after each outcome is.</summary>
public class TranslationWorkerTests
{
    [Theory]
    [InlineData(ProcessOutcomeKind.Progressed)]
    [InlineData(ProcessOutcomeKind.Completed)]
    public void Work_done_goes_on_at_once(ProcessOutcomeKind kind)
    {
        var pause = TranslationWorker.PauseAfter(new ProcessOutcome(kind), translatorConfigured: true, previousQuotaWait: null);

        Assert.Equal(new WorkerPause(TimeSpan.Zero, false), pause);
    }

    /// <summary>
    /// Every unavailable batch reports Failed; going again at once would spend a job's five
    /// attempts within a second of a network blip.
    /// </summary>
    [Fact]
    public void A_failed_batch_waits_a_minute_whatever_the_signal()
    {
        var pause = TranslationWorker.PauseAfter(ProcessOutcome.Failed, translatorConfigured: true, previousQuotaWait: null);

        Assert.Equal(new WorkerPause(TimeSpan.FromMinutes(1), false), pause);
    }

    [Fact]
    public void No_job_waits_for_an_enqueue_at_most_thirty_seconds()
    {
        var pause = TranslationWorker.PauseAfter(ProcessOutcome.Idle, translatorConfigured: true, previousQuotaWait: null);

        Assert.Equal(new WorkerPause(TimeSpan.FromSeconds(30), true), pause);
    }

    [Fact]
    public void An_unconfigured_translator_waits_at_most_five_minutes()
    {
        var pause = TranslationWorker.PauseAfter(ProcessOutcome.Idle, translatorConfigured: false, previousQuotaWait: null);

        Assert.Equal(new WorkerPause(TimeSpan.FromMinutes(5), true), pause);
    }

    /// <summary>The quota is shared by every job, so an enqueue must not cut the pause short.</summary>
    [Fact]
    public void A_quota_refusal_backs_off_and_ignores_the_signal()
    {
        var pause = TranslationWorker.PauseAfter(
            ProcessOutcome.Quota(null), translatorConfigured: true, previousQuotaWait: TimeSpan.FromMinutes(4));

        Assert.Equal(new WorkerPause(TimeSpan.FromMinutes(8), false), pause);
    }
}
