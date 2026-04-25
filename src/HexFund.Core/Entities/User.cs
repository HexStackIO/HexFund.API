namespace HexFund.Core.Entities;

/// <summary>
/// Represents a user of the application
/// </summary>
public class User
{
    public Guid UserId { get; set; }
    public string? EntraObjectId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Set to true the first time the user edits their name via the profile
    /// settings screen. When true, the Entra claim values from login are no
    /// longer used to overwrite the stored name.
    /// </summary>
    public bool HasCustomName { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    // Navigation properties
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<UserCategory> Categories { get; set; } = new List<UserCategory>();
}