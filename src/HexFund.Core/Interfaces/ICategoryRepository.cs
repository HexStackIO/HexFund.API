using HexFund.Core.Entities;

namespace HexFund.Core.Interfaces;

/// <summary>
/// Data access contract for user-defined transaction categories.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Returns all categories belonging to the specified user.</summary>
    Task<IEnumerable<UserCategory>> GetByUserIdAsync(Guid userId);

    /// <summary>Returns a single category by its primary key, or null if not found.</summary>
    Task<UserCategory?> GetByIdAsync(Guid categoryId);

    /// <summary>Persists a new category record.</summary>
    Task CreateAsync(UserCategory category);

    /// <summary>Removes a category by primary key.</summary>
    Task DeleteAsync(Guid categoryId);
}