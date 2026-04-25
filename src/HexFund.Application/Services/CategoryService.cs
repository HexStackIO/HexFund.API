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

    public async Task<IEnumerable<UserCategoryDto>> GetUserCategoriesAsync(Guid userId)
    {
        var categories = await _categoryRepository.GetByUserIdAsync(userId);
        return categories
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto);
    }

    public async Task<UserCategoryDto?> CreateCategoryAsync(
        CreateCategoryRequest request, Guid userId)
    {
        var trimmedName = request.Name.Trim();

        // Enforce uniqueness per user (case-insensitive)
        var existing = await _categoryRepository.GetByUserIdAsync(userId);
        if (existing.Any(c => string.Equals(c.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            return null;

        var category = new UserCategory
        {
            CategoryId = Guid.NewGuid(),
            UserId = userId,
            Name = trimmedName,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _categoryRepository.CreateAsync(category);
        return MapToDto(category);
    }

    public async Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId)
    {
        var category = await _categoryRepository.GetByIdAsync(categoryId);
        if (category == null || category.UserId != userId)
            return false;

        await _categoryRepository.DeleteAsync(categoryId);
        return true;
    }

    private static UserCategoryDto MapToDto(UserCategory c) => new()
    {
        CategoryId = c.CategoryId,
        Name = c.Name,
        CreatedAt = c.CreatedAt,
    };
}