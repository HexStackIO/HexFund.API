using HexFund.Core.Enums;

namespace HexFund.Core.Entities;

/// <summary>
/// A bug report, idea or question submitted from the app. The row doubles as an outbox entry: it is
/// saved first, then a background dispatcher turns it into a private issue. The row id is the only
/// link between an issue and the person who wrote it, and it never leaves the database.
/// </summary>
public class Feedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public FeedbackType Type { get; set; }

    /// <summary>The user text, normalised by the service. Multi-line.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The sanitised diagnostics as JSON, or null when the user switched them off.</summary>
    public string? DiagnosticsJson { get; set; }

    public FeedbackOutboxStatus OutboxStatus { get; set; } = FeedbackOutboxStatus.Pending;

    /// <summary>How many times the dispatcher has claimed this row.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// When the dispatcher may next pick the row up. While a row is claimed this holds the end of the
    /// lease, so rows held by a crashed worker become due again.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>The most recent failure, truncated to 500 characters. Never contains the report text.</summary>
    public string? LastError { get; set; }

    public int? GitHubIssueNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    public User User { get; set; } = null!;
}
