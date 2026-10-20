namespace HexFund.Application.Interfaces;

/// <summary>
/// Entra External ID user management via Microsoft Graph. Behind an interface so the Graph SDK
/// stays in the Infrastructure layer and <c>AuthService</c> can be tested without it.
/// </summary>
public interface IEntraUserService
{
    /// <summary>
    /// Permanently deletes the Entra user (a hard delete, not the recycle bin).
    /// Returns false when the user no longer exists in Entra, which callers treat as non-fatal.
    /// Any other Graph failure (missing consent, outage) is thrown so the caller can abort.
    /// </summary>
    Task<bool> DeleteUserAsync(string entraObjectId);
}
