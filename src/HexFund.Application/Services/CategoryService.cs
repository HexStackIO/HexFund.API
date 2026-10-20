using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Entities;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoryService(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<IReadOnlyList<UserCategoryDto>> GetUserCategoriesAsync(Guid userId)
    {
        var categories = await _categoryRepository.GetByUserIdAsync(userId);
        return categories
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<UserCategoryDto?> CreateCategoryAsync(CreateCategoryRequest request, Guid userId)
    {
        if (await _categoryRepository.ExistsAsync(userId, request.Name))
            return null;

        var category = new UserCategory
        {
            CategoryId = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name
        };

        // The unique index is the backstop if two requests race past the check above.
        return await _categoryRepository.TryCreateAsync(category) ? MapToDto(category) : null;
    }

    public Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId) =>
        _categoryRepository.DeleteForUserAsync(userId, categoryId);

    private static UserCategoryDto MapToDto(UserCategory c) => new()
    {
        CategoryId = c.CategoryId,
        Name = c.Name,
        CreatedAt = c.CreatedAt
    };
}
