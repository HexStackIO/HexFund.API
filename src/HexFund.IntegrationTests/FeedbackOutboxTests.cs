using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HexFund.IntegrationTests;

/// <summary>The outbox queries, against a real PostgreSQL. Skipped unless HEXFUND_TEST_DB is set.</summary>
[Collection(PostgresCollection.Name)]
public class FeedbackOutboxTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly PostgresFixture _db;

    public FeedbackOutboxTests(PostgresFixture db) => _db = db;

    /// <summary>The claim looks at every row in the table, so each test starts from an empty one.</summary>
    private async Task<Guid> ResetAsync()
    {
        await using (var context = _db.CreateContext())
            await context.Feedback.ExecuteDeleteAsync();

        return (await _db.AddUserAsync()).UserId;
    }

    private async Task<List<Feedback>> SeedAsync(Guid userId, int count, FeedbackOutboxStatus status = FeedbackOutboxStatus.Pending,
        TimeSpan? dueIn = null, DateTimeOffset? createdAt = null)
    {
        var rows = Enumerable.Range(0, count).Select(i =>
        {
            var row = DatabaseTests.NewFeedback(userId);
            row.OutboxStatus = status;
            row.NextAttemptAt = DateTimeOffset.UtcNow + (dueIn ?? TimeSpan.FromMinutes(-10)) + TimeSpan.FromSeconds(i);
            row.CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
            return row;
        }).ToList();

        await using var context = _db.CreateContext();
        context.Feedback.AddRange(rows);
        await context.SaveChangesAsync();
        return rows;
    }

    private async Task<IReadOnlyList<Feedback>> ClaimAsync(int batchSize = 10)
    {
        await using var context = _db.CreateContext();
        return await new FeedbackRepository(context).ClaimDueBatchAsync(DateTimeOffset.UtcNow, batchSize, Lease);
    }

    private async Task<Feedback> ReloadAsync(Guid id)
    {
        await using var context = _db.CreateContext();
        return await context.Feedback.SingleAsync(f => f.Id == id);
    }

    [DatabaseFact]
    public async Task Claim_ReturnsTheOldestDueRows_UpToTheBatchSize()
    {
        var userId = await ResetAsync();
        var seeded = await SeedAsync(userId, 15);

        var claimed = await ClaimAsync(batchSize: 10);

        Assert.Equal(10, claimed.Count);
        Assert.Equal(seeded.Take(10).Select(r => r.Id).OrderBy(i => i), claimed.Select(r => r.Id).OrderBy(i => i));
    }

    [DatabaseFact]
    public async Task Claim_IncrementsAttempts_AndReservesTheRowForTheLease()
    {
        var userId = await ResetAsync();
        var row = (await SeedAsync(userId, 1))[0];
        var before = DateTimeOffset.UtcNow;

        var claimed = await ClaimAsync();

        var stored = await ReloadAsync(row.Id);
        Assert.Equal(row.Id, Assert.Single(claimed).Id);
        Assert.Equal(1, claimed[0].Attempts);
        Assert.Equal(1, stored.Attempts);
        Assert.True(stored.NextAttemptAt > before + Lease - TimeSpan.FromSeconds(30));
        Assert.NotNull(stored.ModifiedAt);
    }

    [DatabaseFact]
    public async Task Claim_DoesNotHandOutTheSameRowTwice_UntilTheLeaseExpires()
    {
        var userId = await ResetAsync();
        await SeedAsync(userId, 3);

        Assert.Equal(3, (await ClaimAsync()).Count);
        Assert.Empty(await ClaimAsync());

        await using var context = _db.CreateContext();
        var later = await new FeedbackRepository(context).ClaimDueBatchAsync(
            DateTimeOffset.UtcNow + Lease + TimeSpan.FromMinutes(1), 10, Lease);
        Assert.Equal(3, later.Count);
        Assert.All(later, r => Assert.Equal(2, r.Attempts));
    }

    [DatabaseFact]
    public async Task Claim_IgnoresRowsNotYetDue_AndRowsThatAreNotPending()
    {
        var userId = await ResetAsync();
        await SeedAsync(userId, 2, dueIn: TimeSpan.FromHours(1));
        await SeedAsync(userId, 2, FeedbackOutboxStatus.Sent);
        await SeedAsync(userId, 2, FeedbackOutboxStatus.Failed);
        var due = await SeedAsync(userId, 1);

        var claimed = await ClaimAsync();

        Assert.Equal(due[0].Id, Assert.Single(claimed).Id);
    }

    [DatabaseFact]
    public async Task ConcurrentClaimers_NeverTakeTheSameRow()
    {
        var userId = await ResetAsync();
        var seeded = await SeedAsync(userId, 60);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => ClaimAsync(batchSize: 10))));

        var ids = results.SelectMany(batch => batch.Select(r => r.Id)).ToList();
        Assert.Equal(60, ids.Count);
        Assert.Equal(60, ids.Distinct().Count());
        Assert.Equal(seeded.Select(r => r.Id).OrderBy(i => i), ids.OrderBy(i => i));
    }

    [DatabaseFact]
    public async Task MarkSent_RecordsTheIssueNumber_AndClearsTheError()
    {
        var userId = await ResetAsync();
        var row = (await SeedAsync(userId, 1))[0];

        await using (var context = _db.CreateContext())
        {
            var repository = new FeedbackRepository(context);
            await repository.RecordFailedAttemptAsync(row.Id, "GitHub returned 502", DateTimeOffset.UtcNow, giveUp: false);
            await repository.MarkSentAsync(row.Id, 42);
        }

        var stored = await ReloadAsync(row.Id);
        Assert.Equal(FeedbackOutboxStatus.Sent, stored.OutboxStatus);
        Assert.Equal(42, stored.GitHubIssueNumber);
        Assert.Null(stored.LastError);
    }

    [DatabaseFact]
    public async Task RecordFailedAttempt_SchedulesARetry_OrGivesUp_AndTruncatesTheError()
    {
        var userId = await ResetAsync();
        var rows = await SeedAsync(userId, 2);
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(30);

        await using (var context = _db.CreateContext())
        {
            var repository = new FeedbackRepository(context);
            await repository.RecordFailedAttemptAsync(rows[0].Id, new string('x', 900), retryAt, giveUp: false);
            await repository.RecordFailedAttemptAsync(rows[1].Id, "GitHub returned 422", retryAt, giveUp: true);
        }

        var retrying = await ReloadAsync(rows[0].Id);
        Assert.Equal(FeedbackOutboxStatus.Pending, retrying.OutboxStatus);
        Assert.Equal(500, retrying.LastError!.Length);
        Assert.True(Math.Abs((retrying.NextAttemptAt - retryAt).TotalSeconds) < 1);

        Assert.Equal(FeedbackOutboxStatus.Failed, (await ReloadAsync(rows[1].Id)).OutboxStatus);
    }

    [DatabaseFact]
    public async Task OldestPending_IgnoresDeliveredAndFailedRows()
    {
        var userId = await ResetAsync();
        await using var context = _db.CreateContext();
        var repository = new FeedbackRepository(context);
        Assert.Null(await repository.GetOldestPendingCreatedAtAsync());

        await SeedAsync(userId, 1, FeedbackOutboxStatus.Sent, createdAt: DateTimeOffset.UtcNow.AddDays(-30));
        await SeedAsync(userId, 1, FeedbackOutboxStatus.Failed, createdAt: DateTimeOffset.UtcNow.AddDays(-20));
        var pending = DateTimeOffset.UtcNow.AddHours(-3);
        await SeedAsync(userId, 1, createdAt: pending);

        var oldest = await repository.GetOldestPendingCreatedAtAsync();
        Assert.True(Math.Abs((oldest!.Value - pending).TotalSeconds) < 1);
    }

    [DatabaseFact]
    public async Task Retention_DeletesOnlyDeliveredRowsOlderThanTheCutoff()
    {
        var userId = await ResetAsync();
        var old = DateTimeOffset.UtcNow.AddMonths(-13);
        var recent = DateTimeOffset.UtcNow.AddMonths(-2);
        var oldSent = (await SeedAsync(userId, 1, FeedbackOutboxStatus.Sent, createdAt: old))[0];
        var recentSent = (await SeedAsync(userId, 1, FeedbackOutboxStatus.Sent, createdAt: recent))[0];
        var oldPending = (await SeedAsync(userId, 1, createdAt: old))[0];
        var oldFailed = (await SeedAsync(userId, 1, FeedbackOutboxStatus.Failed, createdAt: old))[0];

        await using var context = _db.CreateContext();
        var deleted = await new FeedbackRepository(context).DeleteSentOlderThanAsync(DateTimeOffset.UtcNow.AddMonths(-12));

        Assert.Equal(1, deleted);
        await using var check = _db.CreateContext();
        var remaining = await check.Feedback.Select(f => f.Id).ToListAsync();
        Assert.DoesNotContain(oldSent.Id, remaining);
        Assert.Contains(recentSent.Id, remaining);
        Assert.Contains(oldPending.Id, remaining);
        Assert.Contains(oldFailed.Id, remaining);
    }
}
