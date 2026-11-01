using HexFund.Application.Interfaces;
using HexFund.Application.Issues;
using Microsoft.Extensions.Logging;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>
/// Stands in for GitHub when <c>GitHub:Enabled</c> is false (local development, tests). Reports
/// still flow through the outbox and are marked sent, with a fake issue number.
/// </summary>
public sealed class NoOpIssueTracker : IIssueTracker
{
    private readonly ILogger<NoOpIssueTracker> _logger;
    private int _lastNumber;

    public NoOpIssueTracker(ILogger<NoOpIssueTracker> logger)
    {
        _logger = logger;
    }

    public Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken)
    {
        var number = Interlocked.Increment(ref _lastNumber);
        _logger.LogInformation("GitHub is disabled; skipped creating an issue (fake number {IssueNumber})", number);
        return Task.FromResult(number);
    }

    public Task<int?> FindIssueByMarkerAsync(string marker, DateTimeOffset createdSince, CancellationToken cancellationToken) =>
        Task.FromResult<int?>(null);
}
