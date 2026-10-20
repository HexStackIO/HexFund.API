using HexFund.Application.Dispatching;
using HexFund.Application.Interfaces;
using HexFund.Application.Issues;

namespace HexFund.Tests.Services;

internal sealed class FakeDispatchSignal : IFeedbackDispatchSignal
{
    private readonly Func<int> _rowCount;

    public FakeDispatchSignal(Func<int> rowCount) => _rowCount = rowCount;

    public int Notifications { get; private set; }
    public int RowsWhenNotified { get; private set; }

    public void Notify()
    {
        Notifications++;
        RowsWhenNotified = _rowCount();
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Scriptable tracker: queue outcomes for create calls; records everything it is asked.</summary>
internal sealed class FakeIssueTracker : IIssueTracker
{
    private readonly Queue<Func<int>> _createOutcomes = new();

    public List<IssueDraft> Created { get; } = new();
    public List<string> Lookups { get; } = new();
    public Dictionary<string, int> ExistingIssues { get; } = new();
    public int NextNumber { get; set; } = 100;

    public void FailNext(IssueTrackerException exception) => _createOutcomes.Enqueue(() => throw exception);

    public void FailNextWithUnexpected() => _createOutcomes.Enqueue(() => throw new InvalidOperationException("boom with secret text"));

    public Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken)
    {
        if (_createOutcomes.Count > 0)
            return Task.FromResult(_createOutcomes.Dequeue()());

        Created.Add(draft);
        return Task.FromResult(NextNumber++);
    }

    public Task<int?> FindIssueByMarkerAsync(string marker, DateTimeOffset createdSince, CancellationToken cancellationToken)
    {
        Lookups.Add(marker);
        return Task.FromResult(ExistingIssues.TryGetValue(marker, out var number) ? number : (int?)null);
    }
}
