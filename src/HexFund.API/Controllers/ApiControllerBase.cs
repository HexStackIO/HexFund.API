using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HexFund.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Returns the Entra object ID as a string — use this for Entra-specific operations
    /// like SyncEntraUserAsync where the object ID is stored as a string.
    /// </summary>
    protected string GetCurrentUserObjectId()
    {
        return User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("User ID claim not found in token.");
    }

    /// <summary>
    /// Returns the internal database UserId as a Guid — use this for all standard
    /// service calls that look up users by their PostgreSQL primary key.
    /// Requires the user to have been synced via SyncEntraUserAsync first.
    /// </summary>
    protected Guid GetCurrentUserId()
    {
        var objectId = GetCurrentUserObjectId();
        // Entra object IDs are valid GUIDs — parse directly
        if (Guid.TryParse(objectId, out var guid))
            return guid;

        throw new InvalidOperationException($"User object ID '{objectId}' is not a valid GUID.");
    }
}