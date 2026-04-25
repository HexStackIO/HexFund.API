using HexFund.Application.DTOs;

namespace HexFund.Application.Interfaces;

/// <summary>
/// Manages user-defined transaction categories.
/// Categories are scoped to a user, not to a specific account, so one list
/// covers all of a user's accounts and enables cross-account reporting.
/// </summary>
public interface ICategoryService
{
    /// <summary>Returns all categories belonging to the specified user, ordered by name.</summary>
    Task<IEnumerable<UserCategoryDto>> GetUserCategoriesAsync(Guid userId);

    /// <summary>
    /// Creates a new category for the user. Returns null if a category with
    /// the same name (case-insensitive) already exists for this user.
    /// </summary>
    Task<UserCategoryDto?> CreateCategoryAsync(CreateCategoryRequest request, Guid userId);

    /// <summary>
    /// Deletes a category. Returns true on success, false if not found or not
    /// owned by the specified user. Does NOT cascade-delete transactions — their
    /// Category string is left as-is so historical data is preserved.
    /// </summary>
    Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId);
}