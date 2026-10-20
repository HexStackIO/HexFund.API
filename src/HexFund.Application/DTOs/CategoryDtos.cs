using System.ComponentModel.DataAnnotations;
using HexFund.Application.Validation;

namespace HexFund.Application.DTOs;

/// <summary>
/// A user-defined transaction category. Categories are scoped to a user (not a
/// specific account), so one list covers all of a user's accounts.
/// </summary>
public class UserCategoryDto
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateCategoryRequest
{
    private string _name = string.Empty;

    [Required(ErrorMessage = "Category name is required")]
    [MaxLength(100, ErrorMessage = "Category name cannot exceed 100 characters")]
    public string Name
    {
        get => _name;
        set => _name = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }
}
