namespace LanguageLab.Application.Translation;

/// <summary>A language-model call that produced no usable answer.</summary>
public abstract class LlmException : Exception
{
    protected LlmException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The provider's own quota or rate limit refused the call. <see cref="RetryAfter"/> is the
/// provider's hint when it gave one — a background caller backs off at least that long.
/// </summary>
public sealed class LlmQuotaException : LlmException
{
    public LlmQuotaException(string message, TimeSpan? retryAfter)
        : base(message) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// No answer for any other reason: no credentials configured, network failure, timeout, a status
/// or finish reason that is not success, or an answer that is not a JSON object.
/// </summary>
public sealed class LlmUnavailableException : LlmException
{
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
