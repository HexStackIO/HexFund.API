namespace HexFund.Application.Dispatching;

/// <summary>When to retry a failed delivery and when to give up.</summary>
public static class FeedbackRetryPolicy
{
    /// <summary>After this many attempts a report is marked failed and needs a human.</summary>
    public const int MaxAttempts = 8;

    private const double JitterFraction = 0.2;

    private static readonly TimeSpan[] Schedule =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6)
    };

    /// <summary>
    /// The wait after the given failed attempt (1-based): 1 min, 5 min, 30 min, 2 h, then 6 h each
    /// time. <paramref name="jitter"/> is a value in [0, 1) that spreads the delay by up to 20% either
    /// way so retries from many reports do not land together.
    /// </summary>
    public static TimeSpan NextDelay(int attempt, double jitter)
    {
        var index = Math.Clamp(attempt, 1, int.MaxValue) - 1;
        var baseDelay = Schedule[Math.Min(index, Schedule.Length - 1)];
        var factor = 1 + (jitter * 2 - 1) * JitterFraction;
        return TimeSpan.FromTicks((long)(baseDelay.Ticks * factor));
    }
}
