using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// The Entra object id from the token. A missing claim means the token is not one of ours,
    /// so it surfaces as a 401 (UnauthorizedAccessException) rather than a 500.
    /// </summary>
    protected string CurrentObjectId =>
        User.FindFirstValue("oid")
        ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("User identity claim not found in token.");

    /// <summary>
    /// The local UserId. It is the Entra object id (see AuthService.SyncEntraUserAsync), so it
    /// is available straight from the token; the user must have called POST api/auth/sync once.
    /// </summary>
    protected Guid CurrentUserId =>
        Guid.TryParse(CurrentObjectId, out var id)
            ? id
            : throw new UnauthorizedAccessException("User identity claim is not a valid GUID.");
}
