using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Core.Interfaces;

namespace HexFund.Tests.Services;

internal sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();

    public Task<User?> GetByIdAsync(Guid userId) => Task.FromResult(Users.FirstOrDefault(u => u.UserId == userId));
    public Task<User?> GetByEntraIdAsync(string entraObjectId) =>
        Task.FromResult(Users.FirstOrDefault(u => u.EntraObjectId == entraObjectId));
    public Task<User> CreateAsync(User user) { Users.Add(user); return Task.FromResult(user); }
    public Task<User> UpdateAsync(User user) => Task.FromResult(user);
    public Task<bool> DeleteAsync(Guid userId) => Task.FromResult(Users.RemoveAll(u => u.UserId == userId) > 0);
}

/// <summary>In-memory outbox. Mirrors the claim, retry and retention semantics of the real repository.</summary>
internal sealed class FakeFeedbackRepository : IFeedbackRepository
{
    public List<Feedback> Rows { get; } = new();

    public Task<Feedback> AddAsync(Feedback feedback) { Rows.Add(feedback); return Task.FromResult(feedback); }

    public Task<Feedback?> GetByIdAsync(Guid id) => Task.FromResult(Rows.FirstOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<Feedback>> ClaimDueBatchAsync(DateTimeOffset now, int batchSize, TimeSpan lease)
    {
        var due = Rows
            .Where(f => f.OutboxStatus == FeedbackOutboxStatus.Pending && f.NextAttemptAt <= now)
            .OrderBy(f => f.NextAttemptAt)
            .Take(batchSize)
            .ToList();

        foreach (var row in due)
        {
            row.Attempts++;
            row.NextAttemptAt = now + lease;
        }

        return Task.FromResult<IReadOnlyList<Feedback>>(due);
    }

    public Task MarkSentAsync(Guid id, int issueNumber)
    {
        var row = Rows.Single(f => f.Id == id);
        row.OutboxStatus = FeedbackOutboxStatus.Sent;
        row.GitHubIssueNumber = issueNumber;
        row.LastError = null;
        return Task.CompletedTask;
    }

    public Task RecordFailedAttemptAsync(Guid id, string error, DateTimeOffset nextAttemptAt, bool giveUp)
    {
        var row = Rows.Single(f => f.Id == id);
        row.OutboxStatus = giveUp ? FeedbackOutboxStatus.Failed : FeedbackOutboxStatus.Pending;
        row.NextAttemptAt = nextAttemptAt;
        row.LastError = error;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetOldestPendingCreatedAtAsync() =>
        Task.FromResult(Rows
            .Where(f => f.OutboxStatus == FeedbackOutboxStatus.Pending)
            .Select(f => (DateTimeOffset?)f.CreatedAt)
            .OrderBy(t => t)
            .FirstOrDefault());

    public Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoff) =>
        Task.FromResult(Rows.RemoveAll(f => f.OutboxStatus == FeedbackOutboxStatus.Sent && f.CreatedAt < cutoff));
}
