namespace HexFund.Core.Entities;

/// <summary>
/// Represents a financial account (checking, savings, etc.)
/// </summary>
public class Account
{
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
