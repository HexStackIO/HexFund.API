using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HexFund.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IEntraUserService _entraUserService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IEntraUserService entraUserService,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _entraUserService = entraUserService;
        _logger = logger;
    }

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        return user == null ? null : MapToUserDto(user);
    }

    /// <summary>
    /// The Entra object id is the identity: it is also used as the local UserId, which is
    /// what lets every controller derive the user straight from the token. Users are never
    /// matched by email, because the email claim is not guaranteed to be verified.
    /// </summary>
    public async Task<UserDto> SyncEntraUserAsync(
        string objectId, string? email, string? firstName, string? lastName)
    {
        if (!Guid.TryParse(objectId, out var userId))
            throw new ArgumentException("Entra object id is not a valid GUID.", nameof(objectId));

        var user = await _userRepository.GetByEntraIdAsync(objectId);

        if (user == null)
        {
            user = new User
            {
                UserId = userId,
                EntraObjectId = objectId,
                Email = email ?? string.Empty,
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                LastLoginAt = DateTimeOffset.UtcNow
            };
            await _userRepository.CreateAsync(user);
        }
        else
        {
            // Entra names only overwrite the stored name until the user edits it locally.
            if (!user.HasCustomName)
            {
                user.FirstName = firstName ?? user.FirstName;
                user.LastName = lastName ?? user.LastName;
            }

            if (!string.IsNullOrEmpty(email))
                user.Email = email;

            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _userRepository.UpdateAsync(user);
        }

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return null;

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.HasCustomName = true; // stops sign-in sync from overwriting this
        await _userRepository.UpdateAsync(user);

        return MapToUserDto(user);
    }

    /// <summary>
    /// Deletes the Entra account first. If that fails unexpectedly we abort before touching
    /// the database so the user can retry with their data intact. A 404 from Entra is
    /// non-fatal (handled in <see cref="IEntraUserService"/>), so a half-finished earlier
    /// attempt can still be completed.
    /// </summary>
    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("DeleteUserAsync: no DB record found for {UserId}", userId);
            return false;
        }

        if (!string.IsNullOrEmpty(user.EntraObjectId))
        {
            try
            {
                await _entraUserService.DeleteUserAsync(user.EntraObjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to delete Entra user {EntraObjectId} for {UserId}. DB records preserved.",
                    user.EntraObjectId, userId);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("User {UserId} has no EntraObjectId, skipping Entra deletion.", userId);
        }

        return await _userRepository.DeleteAsync(userId);
    }

    private static UserDto MapToUserDto(User user) => new()
    {
        UserId = user.UserId,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        CreatedAt = user.CreatedAt
    };
}
