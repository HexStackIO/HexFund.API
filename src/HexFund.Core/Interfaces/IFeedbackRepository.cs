using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

/// <summary>Storage for feedback reports and the outbox state used to forward them.</summary>
public interface IFeedbackRepository
{
    Task<Feedback> AddAsync(Feedback feedback);

    Task<Feedback?> GetByIdAsync(Guid id);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> pending rows that are due at
    /// <paramref name="now"/>. Each claimed row has its attempt count incremented and its
    /// <c>NextAttemptAt</c> pushed to <c>now + lease</c>, so a second dispatcher, or a restart after a
    /// crash, cannot pick up the same row until the lease expires.
    /// </summary>
    Task<IReadOnlyList<Feedback>> ClaimDueBatchAsync(DateTimeOffset now, int batchSize, TimeSpan lease);

    /// <summary>Marks the row sent and records the issue number.</summary>
    Task MarkSentAsync(Guid id, int issueNumber);

    /// <summary>
    /// Records a failed attempt. With <paramref name="giveUp"/> the row becomes <c>Failed</c>;
    /// otherwise it stays <c>Pending</c> and is retried at <paramref name="nextAttemptAt"/>.
    /// </summary>
    Task RecordFailedAttemptAsync(Guid id, string error, DateTimeOffset nextAttemptAt, bool giveUp);

    /// <summary>Creation time of the oldest row still pending, or null if none is.</summary>
    Task<DateTimeOffset?> GetOldestPendingCreatedAtAsync();

    /// <summary>Deletes <c>Sent</c> rows created before the cutoff and returns how many were removed.</summary>
    Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoff);
}
