namespace LanguageLab.Application.Translation.Queue;

public enum ProcessOutcomeKind
{
    /// <summary>No pending job, or no translator configured.</summary>
    Idle,
    /// <summary>One batch translated and the job's cursor moved past it.</summary>
    Progressed,
    /// <summary>The job's pass found no words left and was marked completed.</summary>
    Completed,
    /// <summary>The provider refused for quota; nothing changed.</summary>
    QuotaExceeded,
    /// <summary>The provider could not answer the batch (counted), or the job cannot run at all.</summary>
    Failed,
}

/// <summary>What one TranslationJobProcessor iteration did; <see cref="RetryAfter"/> only for QuotaExceeded.</summary>
public sealed record ProcessOutcome(ProcessOutcomeKind Kind, TimeSpan? RetryAfter = null)
{
    public static readonly ProcessOutcome Idle = new(ProcessOutcomeKind.Idle);
    public static readonly ProcessOutcome Progressed = new(ProcessOutcomeKind.Progressed);
    public static readonly ProcessOutcome Completed = new(ProcessOutcomeKind.Completed);
    public static readonly ProcessOutcome Failed = new(ProcessOutcomeKind.Failed);

    public static ProcessOutcome Quota(TimeSpan? retryAfter) => new(ProcessOutcomeKind.QuotaExceeded, retryAfter);
}
