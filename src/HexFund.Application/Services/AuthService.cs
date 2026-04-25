using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;

    public AuthService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// No longer used — login is handled by Entra External ID via MSAL.
    /// Kept to satisfy interface until legacy endpoints are fully removed.
    /// </summary>
    public Task<AuthResponse?> LoginAsync(LoginRequest request)
        => Task.FromResult<AuthResponse?>(null);

    /// <summary>
    /// No longer used — registration is handled by Entra External ID via MSAL.
    /// Kept to satisfy interface until legacy endpoints are fully removed.
    /// </summary>
    public Task<AuthResponse?> RegisterAsync(RegisterRequest request)
        => Task.FromResult<AuthResponse?>(null);

    public async Task<UserDto?> GetCurrentUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        return user == null ? null : MapToUserDto(user);
    }

    public async Task<UserDto?> SyncEntraUserAsync(
        string objectId, string? email, string? firstName, string? lastName)
    {
        var user = await _userRepository.GetByEntraIdAsync(objectId);

        if (user == null && !string.IsNullOrEmpty(email))
            user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            user = new User
            {
                UserId = Guid.Parse(objectId),
                EntraObjectId = objectId,
                Email = email ?? string.Empty,
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                PasswordHash = string.Empty,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow
            };
            await _userRepository.CreateAsync(user);
        }
        else
        {
            if (string.IsNullOrEmpty(user.EntraObjectId))
                user.EntraObjectId = objectId;

            // Only overwrite name from Entra if the user hasn't customised it locally.
            // Once a user edits their name via UpdateProfileAsync the Entra claim is
            // no longer considered authoritative for display purposes.
            if (!user.HasCustomName)
            {
                user.FirstName = firstName ?? user.FirstName;
                user.LastName = lastName ?? user.LastName;
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _userRepository.UpdateAsync(user);
        }

        return MapToUserDto(user);
    }

    public async Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return null;

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.HasCustomName = true; // prevents sync from overwriting this on next login
        await _userRepository.UpdateAsync(user);

        return MapToUserDto(user);
    }

    private static UserDto MapToUserDto(User user) => new()
    {
        UserId = user.UserId,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        CreatedAt = user.CreatedAt,
    };
}
