using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Route("api/[controller]")]
public class CategoriesController : ApiControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>Returns all categories for the authenticated user, ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories() =>
        Ok(await _categoryService.GetUserCategoriesAsync(CurrentUserId));

    /// <summary>Creates a category. Returns 409 if the user already has one with that name.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(UserCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var category = await _categoryService.CreateCategoryAsync(request, CurrentUserId);

        if (category == null)
            return Problem(
                detail: $"A category named '{request.Name}' already exists.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict");

        return CreatedAtAction(nameof(GetCategories), new { }, category);
    }

    /// <summary>Deletes a category. Transactions keep their category string.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(Guid id) =>
        await _categoryService.DeleteCategoryAsync(id, CurrentUserId) ? NoContent() : NotFound();
}
