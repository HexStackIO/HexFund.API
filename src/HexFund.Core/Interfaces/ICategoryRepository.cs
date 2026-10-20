using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

/// <summary>
/// Data access contract for user-defined transaction categories.
/// </summary>
public interface ICategoryRepository
{
    Task<IReadOnlyList<UserCategory>> GetByUserIdAsync(Guid userId);

    /// <summary>Case-insensitive name check scoped to the user.</summary>
    Task<bool> ExistsAsync(Guid userId, string name);

    /// <summary>Persists the category. Returns false if the unique (user, name) index rejected it.</summary>
    Task<bool> TryCreateAsync(UserCategory category);

    /// <summary>Deletes a category owned by the user. Returns false if not found.</summary>
    Task<bool> DeleteForUserAsync(Guid userId, Guid categoryId);
}
