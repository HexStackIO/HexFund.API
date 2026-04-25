namespace HexFund.Core.Entities;

/// <summary>
/// A user-defined label used to group transactions.
/// Categories are scoped to a User (not a specific Account) so one list
/// covers all of a user's accounts and enables cross-account reporting.
///
/// Deleting a category does NOT cascade to transactions — their Category
/// string field is left as-is so historical data is preserved.
/// </summary>
public class UserCategory
{
    public Guid CategoryId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}