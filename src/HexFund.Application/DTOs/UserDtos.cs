using System.ComponentModel.DataAnnotations;
using HexFund.Application.Validation;

namespace HexFund.Application.DTOs;

public class UserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Updates the user's display name in the local database.
/// Email is intentionally excluded: it is the Entra identity and cannot be changed here.
/// </summary>
public class UpdateProfileRequest
{
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;

    [Required(ErrorMessage = "First name is required")]
    [MaxLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    public string FirstName
    {
        get => _firstName;
        set => _firstName = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }

    [Required(ErrorMessage = "Last name is required")]
    [MaxLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    public string LastName
    {
        get => _lastName;
        set => _lastName = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }
}
