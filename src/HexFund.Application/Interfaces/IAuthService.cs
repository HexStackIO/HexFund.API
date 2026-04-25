using HexFund.Application.DTOs;

namespace HexFund.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<UserDto?> GetCurrentUserAsync(Guid userId);
    Task<UserDto?> SyncEntraUserAsync(string objectId, string? email, string? firstName, string? lastName);

    /// <summary>
    /// Updates the user's first and last name in the local database.
    /// Returns the updated UserDto, or null if the user was not found.
    /// </summary>
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);

    /// <summary>
    /// Permanently deletes the user record and all associated data
    /// (accounts, transactions, categories) via DB cascade.
    /// Returns false if the user was not found.
    /// </summary>
    Task<bool> DeleteUserAsync(Guid userId);
}