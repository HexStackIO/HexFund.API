using System.ComponentModel.DataAnnotations;
using HexFund.Application.Validation;

namespace HexFund.Application.DTOs;

public class AccountDto
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateAccountRequest
{
    private string _accountName = string.Empty;
    private string _currency = "USD";

    [Required(ErrorMessage = "Account name is required")]
    [MaxLength(100, ErrorMessage = "Account name cannot exceed 100 characters")]
    public string AccountName
    {
        get => _accountName;
        set => _accountName = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }

    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Initial balance must be between -1,000,000,000 and 1,000,000,000")]
    public required decimal InitialBalance { get; set; }

    [Required(ErrorMessage = "Currency is required")]
    [RegularExpression(@"^[A-Z]{3}$",
        ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public string Currency
    {
        get => _currency;
        set => _currency = (InputSanitizer.NormalizeText(value) ?? string.Empty).ToUpperInvariant();
    }
}

public class UpdateAccountRequest
{
    private string _accountName = string.Empty;

    [Required(ErrorMessage = "Account name is required")]
    [MaxLength(100, ErrorMessage = "Account name cannot exceed 100 characters")]
    public string AccountName
    {
        get => _accountName;
        set => _accountName = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }

    public required bool IsActive { get; set; }
}
