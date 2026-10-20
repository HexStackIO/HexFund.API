using HexFund.Application.Interfaces;
using HexFund.Application.Issues;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HexFund.Application.Dispatching;

/// <summary>
/// Moves pending reports to the issue tracker: claim a due batch, create each issue, record the
/// result. One call is one pass; the background host decides when to call it.
/// </summary>
/// <remarks>
/// Only ids, statuses and attempt counts are logged, never report text. A row that has been claimed
/// before is looked up in the tracker first, so an issue that was created but not acknowledged
/// (timeout, crash) is adopted instead of duplicated.
/// </remarks>
public class FeedbackDispatchProcessor
{
    public const int BatchSize = 10;

    /// <summary>How long a claimed row is reserved before another worker may take it.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>The oldest report a pass may look back for when searching for an existing issue.</summary>
    private static readonly TimeSpan SearchSlack = TimeSpan.FromMinutes(5);

    private readonly IFeedbackRepository _repository;
    private readonly IIssueTracker _tracker;
    private readonly TimeProvider _time;
    private readonly Func<double> _jitter;
    private readonly ILogger<FeedbackDispatchProcessor> _logger;

    public FeedbackDispatchProcessor(
        IFeedbackRepository repository,
        IIssueTracker tracker,
        TimeProvider time,
        ILogger<FeedbackDispatchProcessor> logger)
        : this(repository, tracker, time, logger, Random.Shared.NextDouble)
    {
    }

    public FeedbackDispatchProcessor(
        IFeedbackRepository repository,
        IIssueTracker tracker,
        TimeProvider time,
        ILogger<FeedbackDispatchProcessor> logger,
        Func<double> jitter)
    {
        _repository = repository;
        _tracker = tracker;
        _time = time;
        _logger = logger;
        _jitter = jitter;
    }

    /// <summary>Processes every report that is due now. Returns how many were handled.</summary>
    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var handled = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _repository.ClaimDueBatchAsync(_time.GetUtcNow(), BatchSize, Lease);
            foreach (var row in batch)
            {
                await ProcessAsync(row, cancellationToken);
                handled++;
            }

            if (batch.Count < BatchSize)
                break;
        }

        return handled;
    }

    /// <summary>How long the oldest pending report has been waiting, or null if none is.</summary>
    public async Task<TimeSpan?> GetOldestPendingAgeAsync()
    {
        var oldest = await _repository.GetOldestPendingCreatedAtAsync();
        return oldest.HasValue ? _time.GetUtcNow() - oldest.Value : null;
    }

    private async Task ProcessAsync(Feedback row, CancellationToken cancellationToken)
    {
        try
        {
            int? issueNumber = null;

            if (row.Attempts > 1)
            {
                issueNumber = await _tracker.FindIssueByMarkerAsync(
                    IssueDraftBuilder.MarkerFor(row.Id), row.CreatedAt - SearchSlack, cancellationToken);

                if (issueNumber.HasValue)
                    _logger.LogInformation(
                        "Feedback {FeedbackId} already has issue #{IssueNumber}; adopting it", row.Id, issueNumber);
            }

            issueNumber ??= await _tracker.CreateIssueAsync(IssueDraftBuilder.Build(row), cancellationToken);

            await _repository.MarkSentAsync(row.Id, issueNumber.Value);
            _logger.LogInformation(
                "Feedback {FeedbackId} sent as issue #{IssueNumber} after {Attempts} attempt(s)",
                row.Id, issueNumber, row.Attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. The row keeps its lease and becomes due again afterwards.
            throw;
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(row, ex);
        }
    }

    private async Task RecordFailureAsync(Feedback row, Exception ex)
    {
        var tracker = ex as IssueTrackerException;
        var permanent = tracker is { IsTransient: false };
        var giveUp = permanent || row.Attempts >= FeedbackRetryPolicy.MaxAttempts;

        var delay = FeedbackRetryPolicy.NextDelay(row.Attempts, _jitter());
        if (tracker?.RetryAfter is { } retryAfter && retryAfter > delay)
            delay = retryAfter;

        // Exception messages from our own tracker are safe to store; anything else is reduced to its type.
        var error = tracker?.Message ?? $"Unexpected {ex.GetType().Name}";

        await _repository.RecordFailedAttemptAsync(row.Id, error, _time.GetUtcNow() + delay, giveUp);

        if (giveUp)
            _logger.LogError(
                "Feedback {FeedbackId} failed permanently after {Attempts} attempt(s): {Error}",
                row.Id, row.Attempts, error);
        else
            _logger.LogWarning(
                "Feedback {FeedbackId} attempt {Attempts} failed: {Error}. Retrying in {Delay}",
                row.Id, row.Attempts, error, delay);
    }
}
