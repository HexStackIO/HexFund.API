namespace HexFund.Core.Entities;

/// <summary>
/// A user-defined label for grouping transactions. Scoped to the user rather than an account,
/// so one list serves all of the user's accounts. Names are unique per user.
/// </summary>
/// <remarks>
/// Transactions store the category as plain text, so deleting a category leaves existing
/// transactions untouched and history is preserved.
/// </remarks>
public class UserCategory
{
    public Guid CategoryId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
