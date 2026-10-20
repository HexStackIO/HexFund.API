namespace HexFund.Core.Enums;

/// <summary>
/// Where a feedback report is in its journey to the issue tracker. The numeric values are stored
/// in the database, so they must not be renumbered. New values are appended.
/// </summary>
public enum FeedbackOutboxStatus
{
    /// <summary>Waiting for the dispatcher to create the issue.</summary>
    Pending = 0,

    /// <summary>The issue exists and <see cref="Entities.Feedback.GitHubIssueNumber"/> is set.</summary>
    Sent = 1,

    /// <summary>The dispatcher gave up after the attempt cap. Needs a human.</summary>
    Failed = 2
}
