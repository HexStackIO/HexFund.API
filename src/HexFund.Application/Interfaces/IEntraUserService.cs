namespace HexFund.Application.Interfaces;

/// <summary>
/// Abstracts Microsoft Graph API calls for Entra External ID user management.
/// Keeping this behind an interface means AuthService stays testable and the
/// Graph SDK dependency lives only in the Infrastructure layer.
/// </summary>
public interface IEntraUserService
{
    /// <summary>
    /// Permanently deletes the Entra External ID user identified by
    /// <paramref name="entraObjectId"/>. This hard-deletes the account from
    /// the directory — it does not go to the recycle bin.
    ///
    /// Returns true on success.
    /// Returns false if the user was not found in Entra (already deleted or
    /// never synced) — callers should treat this as a non-fatal condition.
    /// Throws on unexpected Graph API errors so the caller can surface them.
    /// </summary>
    Task<bool> DeleteUserAsync(string entraObjectId);
}
