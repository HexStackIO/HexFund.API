namespace HexFund.Core.Entities;

/// <summary>
/// The local record of a signed-in user. Identity lives in Entra External ID: <see cref="UserId"/>
/// equals the Entra object id, so it can be read straight from the access token.
/// </summary>
public class User
{
    public Guid UserId { get; set; }

    /// <summary>The Entra object id as text. Unique.</summary>
    public string? EntraObjectId { get; set; }

    /// <summary>Copied from the token on each sync. Not unique and not used to look users up.</summary>
    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// True once the user edits their name in the app. From then on the name from the Entra token
    /// no longer overwrites the stored one during sync.
    /// </summary>
    public bool HasCustomName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<UserCategory> Categories { get; set; } = new List<UserCategory>();
}
