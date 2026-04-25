using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Authorize]
[Route("api/[controller]")]
public class CategoriesController : ApiControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ICacheService    _cacheService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(
        ICategoryService categoryService,
        ICacheService cacheService,
        ILogger<CategoriesController> logger)
    {
        _categoryService = categoryService;
        _cacheService    = cacheService;
        _logger          = logger;
    }

    /// <summary>Returns all categories for the authenticated user, ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories()
    {
        var userId     = GetCurrentUserId();
        var categories = await _categoryService.GetUserCategoriesAsync(userId);
        return Ok(categories);
    }

    /// <summary>
    /// Creates a new category. Returns 409 Conflict if a category with the same
    /// name already exists for this user.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var userId   = GetCurrentUserId();
        var category = await _categoryService.CreateCategoryAsync(request, userId);

        if (category == null)
            return Conflict(new { message = $"A category named '{request.Name}' already exists." });

        _logger.LogInformation("Category '{Name}' created for user {UserId}",
            category.Name, userId);

        return CreatedAtAction(nameof(GetCategories), new { }, category);
    }

    /// <summary>
    /// Deletes a category. Existing transactions that referenced this category
    /// retain their category string — historical data is not modified.
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var userId  = GetCurrentUserId();
        var success = await _categoryService.DeleteCategoryAsync(id, userId);

        if (!success)
            return NotFound();

        _logger.LogInformation("Category {CategoryId} deleted by user {UserId}", id, userId);
        return NoContent();
    }
}
