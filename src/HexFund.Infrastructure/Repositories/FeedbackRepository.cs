using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HexFund.Infrastructure.Repositories;

public class FeedbackRepository : IFeedbackRepository
{
    private const int MaxErrorLength = 500;

    private readonly HexFundDbContext _context;

    public FeedbackRepository(HexFundDbContext context)
    {
        _context = context;
    }

    public async Task<Feedback> AddAsync(Feedback feedback)
    {
        if (feedback.NextAttemptAt == default)
            feedback.NextAttemptAt = DateTimeOffset.UtcNow;

        _context.Feedback.Add(feedback);
        await _context.SaveChangesAsync();
        return feedback;
    }

    public async Task<Feedback?> GetByIdAsync(Guid id) =>
        await _context.Feedback.FirstOrDefaultAsync(f => f.Id == id);

    // One statement selects and updates the rows. SKIP LOCKED means a concurrent claimer skips rows
    // this one has locked instead of waiting, so two dispatchers can never take the same row.
    public async Task<IReadOnlyList<Feedback>> ClaimDueBatchAsync(
        DateTimeOffset now, int batchSize, TimeSpan lease)
    {
        var leaseUntil = now + lease;
        var pending = (int)FeedbackOutboxStatus.Pending;

        var claimed = await _context.Feedback
            .FromSqlInterpolated($"""
                UPDATE "Feedback"
                SET "Attempts" = "Attempts" + 1,
                    "NextAttemptAt" = {leaseUntil},
                    "ModifiedAt" = {now}
                WHERE "Id" IN (
                    SELECT "Id" FROM "Feedback"
                    WHERE "OutboxStatus" = {pending} AND "NextAttemptAt" <= {now}
                    ORDER BY "NextAttemptAt"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """)
            .ToListAsync();

        // RETURNING does not preserve the subquery order, so restore a stable, oldest-first order.
        return claimed.OrderBy(f => f.CreatedAt).ToList();
    }

    public async Task MarkSentAsync(Guid id, int issueNumber)
    {
        var now = DateTimeOffset.UtcNow;
        await _context.Feedback
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(f => f.OutboxStatus, FeedbackOutboxStatus.Sent)
                .SetProperty(f => f.GitHubIssueNumber, issueNumber)
                .SetProperty(f => f.LastError, (string?)null)
                .SetProperty(f => f.ModifiedAt, now));
    }

    public async Task RecordFailedAttemptAsync(Guid id, string error, DateTimeOffset nextAttemptAt, bool giveUp)
    {
        var now = DateTimeOffset.UtcNow;
        var trimmed = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        var status = giveUp ? FeedbackOutboxStatus.Failed : FeedbackOutboxStatus.Pending;

        await _context.Feedback
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(f => f.OutboxStatus, status)
                .SetProperty(f => f.NextAttemptAt, nextAttemptAt)
                .SetProperty(f => f.LastError, trimmed)
                .SetProperty(f => f.ModifiedAt, now));
    }

    public async Task<DateTimeOffset?> GetOldestPendingCreatedAtAsync() =>
        await _context.Feedback
            .Where(f => f.OutboxStatus == FeedbackOutboxStatus.Pending)
            .OrderBy(f => f.CreatedAt)
            .Select(f => (DateTimeOffset?)f.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoff) =>
        await _context.Feedback
            .Where(f => f.OutboxStatus == FeedbackOutboxStatus.Sent && f.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
}
