using HexFund.Application.Dispatching;
using HexFund.Application.Issues;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Services;

public class FeedbackDispatchProcessorTests
{
    private readonly FakeFeedbackRepository _repository = new();
    private readonly FakeIssueTracker _tracker = new();
    private readonly FakeClock _clock = new(Utc(2026, 10, 20, 12));
    private readonly FeedbackDispatchProcessor _processor;

    public FeedbackDispatchProcessorTests()
    {
        // A jitter of 0.5 means "no spread", so delays equal the schedule exactly.
        _processor = new FeedbackDispatchProcessor(
            _repository, _tracker, _clock, NullLogger<FeedbackDispatchProcessor>.Instance, () => 0.5);
    }

    private Feedback AddRow(DateTimeOffset? due = null)
    {
        var row = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = FeedbackType.Bug,
            Body = "The calendar shows rent on the wrong day.",
            OutboxStatus = FeedbackOutboxStatus.Pending,
            NextAttemptAt = due ?? _clock.GetUtcNow(),
            CreatedAt = _clock.GetUtcNow()
        };
        _repository.Rows.Add(row);
        return row;
    }

    [Fact]
    public async Task DueReport_IsSentAndMarkedWithItsIssueNumber()
    {
        var row = AddRow();

        var handled = await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(1, handled);
        Assert.Equal(FeedbackOutboxStatus.Sent, row.OutboxStatus);
        Assert.Equal(100, row.GitHubIssueNumber);
        Assert.Single(_tracker.Created);
        Assert.Contains(IssueDraftBuilder.MarkerFor(row.Id), _tracker.Created[0].Body);
        Assert.Empty(_tracker.Lookups);
    }

    [Fact]
    public async Task ReportsNotYetDue_AreLeftAlone()
    {
        var row = AddRow(due: _clock.GetUtcNow().AddMinutes(10));

        Assert.Equal(0, await _processor.DrainAsync(CancellationToken.None));
        Assert.Equal(FeedbackOutboxStatus.Pending, row.OutboxStatus);
        Assert.Empty(_tracker.Created);
    }

    [Fact]
    public async Task SecondPass_DoesNotSendTheSameReportAgain()
    {
        AddRow();

        await _processor.DrainAsync(CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        Assert.Single(_tracker.Created);
    }

    [Fact]
    public async Task MoreThanOneBatch_IsDrainedInASinglePass()
    {
        for (var i = 0; i < FeedbackDispatchProcessor.BatchSize + 4; i++)
            AddRow();

        var handled = await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackDispatchProcessor.BatchSize + 4, handled);
        Assert.All(_repository.Rows, r => Assert.Equal(FeedbackOutboxStatus.Sent, r.OutboxStatus));
    }

    [Fact]
    public async Task TransientFailure_ReschedulesThenSucceedsOnTheNextAttempt()
    {
        var row = AddRow();
        _tracker.FailNext(new IssueTrackerException("GitHub returned 502", isTransient: true));

        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackOutboxStatus.Pending, row.OutboxStatus);
        Assert.Equal("GitHub returned 502", row.LastError);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(1), row.NextAttemptAt);

        // Not due yet.
        Assert.Equal(0, await _processor.DrainAsync(CancellationToken.None));

        _clock.Advance(TimeSpan.FromMinutes(1));
        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackOutboxStatus.Sent, row.OutboxStatus);
        Assert.Null(row.LastError);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task Backoff_FollowsTheScheduleAcrossRepeatedFailures()
    {
        var row = AddRow();
        var expected = new[] { 1, 5, 30, 120, 360, 360, 360 };

        foreach (var minutes in expected)
        {
            _tracker.FailNext(new IssueTrackerException("GitHub returned 503", isTransient: true));
            await _processor.DrainAsync(CancellationToken.None);

            Assert.Equal(FeedbackOutboxStatus.Pending, row.OutboxStatus);
            Assert.Equal(_clock.GetUtcNow().AddMinutes(minutes), row.NextAttemptAt);
            _clock.Advance(TimeSpan.FromMinutes(minutes));
        }
    }

    [Fact]
    public async Task AfterEightAttempts_TheReportIsMarkedFailed()
    {
        var row = AddRow();

        for (var attempt = 1; attempt <= FeedbackRetryPolicy.MaxAttempts; attempt++)
        {
            _tracker.FailNext(new IssueTrackerException("GitHub returned 503", isTransient: true));
            await _processor.DrainAsync(CancellationToken.None);
            _clock.Advance(TimeSpan.FromHours(7));
        }

        Assert.Equal(FeedbackOutboxStatus.Failed, row.OutboxStatus);
        Assert.Equal(FeedbackRetryPolicy.MaxAttempts, row.Attempts);

        // A failed report is never picked up again.
        Assert.Equal(0, await _processor.DrainAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PermanentFailure_GivesUpImmediately()
    {
        var row = AddRow();
        _tracker.FailNext(new IssueTrackerException("GitHub returned 422", isTransient: false));

        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackOutboxStatus.Failed, row.OutboxStatus);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task RateLimitDelay_LongerThanTheBackoff_IsHonoured()
    {
        var row = AddRow();
        _tracker.FailNext(new IssueTrackerException("GitHub returned 429", isTransient: true, retryAfter: TimeSpan.FromMinutes(45)));

        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(_clock.GetUtcNow().AddMinutes(45), row.NextAttemptAt);
    }

    [Fact]
    public async Task UnexpectedErrors_AreRetried_AndOnlyTheirTypeIsStored()
    {
        var row = AddRow();
        _tracker.FailNextWithUnexpected();

        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackOutboxStatus.Pending, row.OutboxStatus);
        Assert.Equal("Unexpected InvalidOperationException", row.LastError);
        Assert.DoesNotContain("secret", row.LastError);
    }

    [Fact]
    public async Task RetryAfterAmbiguousFailure_AdoptsTheIssueThatWasAlreadyCreated()
    {
        var row = AddRow();
        _tracker.FailNext(new IssueTrackerException("GitHub request timed out", isTransient: true));
        await _processor.DrainAsync(CancellationToken.None);

        // The request actually succeeded on GitHub before the timeout.
        _tracker.ExistingIssues[IssueDraftBuilder.MarkerFor(row.Id)] = 77;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _processor.DrainAsync(CancellationToken.None);

        Assert.Equal(FeedbackOutboxStatus.Sent, row.OutboxStatus);
        Assert.Equal(77, row.GitHubIssueNumber);
        Assert.Empty(_tracker.Created);
        Assert.Single(_tracker.Lookups);
    }

    [Fact]
    public async Task FirstAttempt_DoesNotSearchForAnExistingIssue()
    {
        AddRow();
        await _processor.DrainAsync(CancellationToken.None);
        Assert.Empty(_tracker.Lookups);
    }

    [Fact]
    public async Task OldestPendingAge_ReportsHowLongTheBacklogHasWaited()
    {
        Assert.Null(await _processor.GetOldestPendingAgeAsync());

        AddRow(due: _clock.GetUtcNow().AddDays(1));
        _clock.Advance(TimeSpan.FromMinutes(90));

        Assert.Equal(TimeSpan.FromMinutes(90), await _processor.GetOldestPendingAgeAsync());
    }

    [Fact]
    public async Task Retention_DeletesOnlySentReportsOlderThanTheCutoff()
    {
        var old = DateTimeOffset.UtcNow.AddMonths(-13);
        var recent = DateTimeOffset.UtcNow.AddMonths(-2);
        _repository.Rows.AddRange(new[]
        {
            Row(FeedbackOutboxStatus.Sent, old),
            Row(FeedbackOutboxStatus.Sent, recent),
            Row(FeedbackOutboxStatus.Pending, old),
            Row(FeedbackOutboxStatus.Failed, old)
        });

        var retention = new FeedbackRetentionProcessor(
            _repository, TimeProvider.System, Options.Create(new FeedbackOptions { RetentionMonths = 12 }),
            NullLogger<FeedbackRetentionProcessor>.Instance);

        var deleted = await retention.RunAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(3, _repository.Rows.Count);
        Assert.DoesNotContain(_repository.Rows, r => r.OutboxStatus == FeedbackOutboxStatus.Sent && r.CreatedAt == old);
    }

    private static Feedback Row(FeedbackOutboxStatus status, DateTimeOffset created) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Body = "text text text", OutboxStatus = status, CreatedAt = created
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    [InlineData(5, 360)]
    [InlineData(6, 360)]
    [InlineData(50, 360)]
    public void RetryPolicy_ScheduleIsOneFiveThirtyMinutesTwoSixHours(int attempt, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), FeedbackRetryPolicy.NextDelay(attempt, 0.5));

    [Fact]
    public void RetryPolicy_JitterSpreadsTheDelayByTwentyPercent()
    {
        var low = FeedbackRetryPolicy.NextDelay(3, 0);
        var high = FeedbackRetryPolicy.NextDelay(3, 1);

        Assert.Equal(TimeSpan.FromMinutes(24), low);
        Assert.Equal(TimeSpan.FromMinutes(36), high);
    }
}
