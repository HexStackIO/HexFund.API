namespace HexFund.Application.Dispatching;

/// <summary>
/// A failure talking to the issue tracker. The message is safe to store and log: it never contains
/// request or response bodies, tokens, or report text.
/// </summary>
public sealed class IssueTrackerException : Exception
{
    public IssueTrackerException(string message, bool isTransient, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }

    /// <summary>True when trying again later may succeed (outage, rate limit, configuration being fixed).</summary>
    public bool IsTransient { get; }

    /// <summary>How long the tracker asked callers to wait, when it said so.</summary>
    public TimeSpan? RetryAfter { get; }
}
