using HexFund.Core.Enums;

namespace HexFund.Core.Entities;

public class Transaction
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Amount of the transaction. Positive values = income, Negative values = expense
    /// </summary>
    public decimal Amount { get; set; }

    public string? Category { get; set; }
    public FrequencyType Frequency { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional hex color chosen by the user (e.g. "#FF5733").
    /// Null means "use default" — red for expense, green for income.
    /// </summary>
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// Set when this transaction was created by an Amend operation.
    /// Points to the original transaction that was end-dated to make room for this one.
    /// Null on every transaction that was never amended.
    /// </summary>
    public Guid? PredecessorTransactionId { get; set; }

    public Account Account { get; set; } = null!;
}
