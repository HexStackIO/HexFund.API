using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Route("api/[controller]")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Called after MSAL sign-in to sync the Entra user into the local database.
    /// Creates the user record if it does not exist yet.
    /// </summary>
    [HttpPost("sync")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncUser()
    {
        var objectId  = GetCurrentUserObjectId();
        var email     = User.FindFirst("preferred_username")?.Value
                     ?? User.FindFirst("email")?.Value;
        var firstName = User.FindFirst("given_name")?.Value;
        var lastName  = User.FindFirst("family_name")?.Value;

        var user = await _authService.SyncEntraUserAsync(objectId, email, firstName, lastName);
        return Ok(user);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        _logger.LogInformation("User logged out: {UserId}", GetCurrentUserObjectId());
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var user = await _authService.GetCurrentUserAsync(GetCurrentUserId());
        return user == null ? NotFound() : Ok(user);
    }

    /// <summary>
    /// Updates the authenticated user's display name.
    /// Email cannot be changed here as it is the Entra identity.
    /// </summary>
    [HttpPut("profile")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        // Validation handled by ValidationActionFilter
        var userId  = GetCurrentUserId();
        var updated = await _authService.UpdateProfileAsync(userId, request);

        if (updated == null)
            return NotFound(new { message = "User not found." });

        _logger.LogInformation("Profile updated for user {UserId}", userId);
        return Ok(updated);
    }

    /// <summary>
    /// Permanently deletes the authenticated user's account and all associated
    /// data: accounts, transactions (including amendment history), and categories.
    ///
    /// Deletion is irreversible. The cascade is handled at the database level:
    ///   User → Accounts (CASCADE) → Transactions (CASCADE)
    ///   User → UserCategories (CASCADE)
    ///
    /// The client is responsible for revoking its local MSAL token cache after
    /// receiving a 204 response. Google Play / App Store data deletion policy
    /// requires this endpoint to be reachable from within the app itself.
    /// </summary>
    [HttpDelete("user")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser()
    {
        var userId = GetCurrentUserId();

        _logger.LogWarning(
            "Account deletion requested for user {UserId}. All data will be permanently removed.",
            userId);

        var deleted = await _authService.DeleteUserAsync(userId);

        if (!deleted)
        {
            _logger.LogWarning("DeleteUser: user {UserId} not found", userId);
            return NotFound(new { message = "User not found." });
        }

        _logger.LogWarning("Account permanently deleted for user {UserId}", userId);
        return NoContent();
    }
}
