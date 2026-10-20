using HexFund.Core.Enums;

namespace HexFund.Core.Entities;

/// <summary>
/// A one-time or recurring transaction. A recurring transaction is stored as a single template
/// row; its occurrences are computed on demand and never persisted.
/// </summary>
public class Transaction
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Positive for income, negative for an expense.</summary>
    public decimal Amount { get; set; }

    public string? Category { get; set; }
    public FrequencyType Frequency { get; set; }

    /// <summary>First occurrence. Recurring series keep this date's time of day.</summary>
    public DateTimeOffset StartDate { get; set; }

    /// <summary>Last date on which the series may occur (inclusive). Null means open-ended.</summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>Inactive transactions are ignored by balances and the calendar.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional user-chosen colour in #RGB or #RRGGBB form, stored uppercase.
    /// Null lets the client pick a default (green for income, red for expense).
    /// </summary>
    public string? Color { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// The transaction this one replaced when it was created by an amendment. The predecessor is
    /// end-dated the day before this one starts. Null if the transaction was never amended.
    /// </summary>
    public Guid? PredecessorTransactionId { get; set; }

    public Account Account { get; set; } = null!;
}
