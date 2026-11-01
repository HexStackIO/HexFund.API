using HexFund.Application.Interfaces;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>
/// Lets the submit path wake the dispatcher. Several notifications before the dispatcher runs
/// collapse into one wake-up, which is enough because a pass drains everything that is due.
/// </summary>
public sealed class FeedbackDispatchSignal : IFeedbackDispatchSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    /// <summary>Waits for a notification or the timeout, whichever comes first.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
