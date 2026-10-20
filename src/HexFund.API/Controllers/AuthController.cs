using HexFund.API.Extensions;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncUser()
    {
        var email = User.FindFirst("preferred_username")?.Value ?? User.FindFirst("email")?.Value;
        var firstName = User.FindFirst("given_name")?.Value;
        var lastName = User.FindFirst("family_name")?.Value;

        return Ok(await _authService.SyncEntraUserAsync(CurrentObjectId, email, firstName, lastName));
    }

    /// <summary>
    /// Sign-out is client-side (MSAL token cache); this exists so the client has a
    /// server-side hook and the sign-out shows up in the logs.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        _logger.LogInformation("User logged out: {UserId}", CurrentObjectId);
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser()
    {
        var user = await _authService.GetCurrentUserAsync(CurrentUserId);
        return user == null ? NotFound() : Ok(user);
    }

    /// <summary>Updates the display name. Email is the Entra identity and cannot be changed here.</summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var updated = await _authService.UpdateProfileAsync(CurrentUserId, request);
        return updated == null ? NotFound() : Ok(updated);
    }

    /// <summary>
    /// Permanently deletes the Entra identity and all of the user's data (accounts,
    /// transactions including amendment history, categories). Irreversible. The client must
    /// clear its MSAL token cache after the 204. Required by Google Play / App Store
    /// account-deletion policies.
    /// </summary>
    [HttpDelete("user")]
    [EnableRateLimiting(ApiPolicies.AccountDeletionRateLimit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser()
    {
        var userId = CurrentUserId;
        _logger.LogWarning("Account deletion requested for user {UserId}", userId);

        if (!await _authService.DeleteUserAsync(userId))
            return NotFound();

        _logger.LogWarning("Account permanently deleted for user {UserId}", userId);
        return NoContent();
    }
}
