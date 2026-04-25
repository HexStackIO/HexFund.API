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
}
