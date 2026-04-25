using HexFund.Application.Validation;

namespace HexFund.Application.DTOs;

using HexFund.Core.Enums;
using System.ComponentModel.DataAnnotations;

// ── Auth DTOs ─────────────────────────────────────────────────────────────────

public class RegisterRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    [MaxLength(128, ErrorMessage = "Password cannot exceed 128 characters")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "First name is required")]
    [MaxLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    [RegularExpression(@"^[\p{L}\p{M}'\-\s]{1,100}$",
        ErrorMessage = "First name contains invalid characters")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [MaxLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    [RegularExpression(@"^[\p{L}\p{M}'\-\s]{1,100}$",
        ErrorMessage = "Last name contains invalid characters")]
    public string LastName { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [MaxLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MaxLength(128, ErrorMessage = "Password cannot exceed 128 characters")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Updates the user's display name in the local database.
/// Email is intentionally excluded — it is the Entra identity and cannot
/// be changed here. Name changes are reflected immediately in the app;
/// they will also be pulled into Entra on the next sync login.
/// </summary>
public class UpdateProfileRequest : IValidatableObject
{
    [Required(ErrorMessage = "First name is required")]
    [MaxLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [MaxLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    public string LastName { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InputSanitizer.ContainsSqlInjectionPatterns(FirstName))
            yield return new ValidationResult(
                "First name contains invalid characters.", new[] { nameof(FirstName) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(LastName))
            yield return new ValidationResult(
                "Last name contains invalid characters.", new[] { nameof(LastName) });
    }
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public UserDto User { get; set; } = null!;
}

// ── User DTOs ─────────────────────────────────────────────────────────────────

public class UserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

// ── Account DTOs ──────────────────────────────────────────────────────────────

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

public class CreateAccountRequest : IValidatableObject
{
    [Required(ErrorMessage = "Account name is required")]
    [MinLength(1, ErrorMessage = "Account name cannot be empty")]
    [MaxLength(100, ErrorMessage = "Account name cannot exceed 100 characters")]
    public string AccountName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Initial balance is required")]
    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Initial balance must be between -1,000,000,000 and 1,000,000,000")]
    public decimal InitialBalance { get; set; }

    [Required(ErrorMessage = "Currency is required")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be exactly 3 characters")]
    [RegularExpression(@"^[A-Z]{3}$",
        ErrorMessage = "Currency must be a valid 3-letter ISO code (e.g., USD, EUR)")]
    public string Currency { get; set; } = "USD";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InputSanitizer.ContainsSqlInjectionPatterns(AccountName))
            yield return new ValidationResult(
                "Account name contains invalid characters.",
                new[] { nameof(AccountName) });
    }
}

public class UpdateAccountRequest : IValidatableObject
{
    [Required(ErrorMessage = "Account name is required")]
    [MinLength(1, ErrorMessage = "Account name cannot be empty")]
    [MaxLength(100, ErrorMessage = "Account name cannot exceed 100 characters")]
    public string AccountName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InputSanitizer.ContainsSqlInjectionPatterns(AccountName))
            yield return new ValidationResult(
                "Account name contains invalid characters.",
                new[] { nameof(AccountName) });
    }
}

// ── Transaction DTOs ──────────────────────────────────────────────────────────

public class TransactionDto
{
    public Guid TransactionId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public int Frequency { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string? Color { get; set; }
    public Guid? PredecessorTransactionId { get; set; }
}

[ValidDateRange]
public class CreateTransactionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Description is required")]
    [MinLength(1, ErrorMessage = "Description cannot be empty")]
    [MaxLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Amount is required")]
    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Amount must be between -1,000,000,000 and 1,000,000,000")]
    public decimal Amount { get; set; }

    [MaxLength(100, ErrorMessage = "Category cannot exceed 100 characters")]
    public string? Category { get; set; }

    [Required(ErrorMessage = "Frequency is required")]
    [Range(0, 6, ErrorMessage = "Invalid frequency value")]
    public int Frequency { get; set; }

    [Required(ErrorMessage = "Start date is required")]
    public DateTimeOffset StartDate { get; set; }

    public DateTimeOffset? EndDate { get; set; }

    [MaxLength(7, ErrorMessage = "Color must be a valid hex value (e.g. #FF5733)")]
    public string? Color { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount == 0)
            yield return new ValidationResult(
                "Amount cannot be zero.", new[] { nameof(Amount) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Description))
            yield return new ValidationResult(
                "Description contains invalid characters.", new[] { nameof(Description) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Category))
            yield return new ValidationResult(
                "Category contains invalid characters.", new[] { nameof(Category) });

        if (Color != null && InputSanitizer.SanitizeHexColor(Color) == null)
            yield return new ValidationResult(
                "Color must be a valid hex color (e.g. #FF5733 or #F53).",
                new[] { nameof(Color) });
    }
}

[ValidDateRange]
public class UpdateTransactionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Description is required")]
    [MinLength(1, ErrorMessage = "Description cannot be empty")]
    [MaxLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Amount is required")]
    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Amount must be between -1,000,000,000 and 1,000,000,000")]
    public decimal Amount { get; set; }

    [MaxLength(100, ErrorMessage = "Category cannot exceed 100 characters")]
    public string? Category { get; set; }

    [Required(ErrorMessage = "Frequency is required")]
    [Range(0, 6, ErrorMessage = "Invalid frequency value")]
    public int Frequency { get; set; }

    [Required(ErrorMessage = "Start date is required")]
    public DateTimeOffset StartDate { get; set; }

    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; }

    [MaxLength(7, ErrorMessage = "Color must be a valid hex value (e.g. #FF5733)")]
    public string? Color { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount == 0)
            yield return new ValidationResult(
                "Amount cannot be zero.", new[] { nameof(Amount) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Description))
            yield return new ValidationResult(
                "Description contains invalid characters.", new[] { nameof(Description) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Category))
            yield return new ValidationResult(
                "Category contains invalid characters.", new[] { nameof(Category) });

        if (Color != null && InputSanitizer.SanitizeHexColor(Color) == null)
            yield return new ValidationResult(
                "Color must be a valid hex color (e.g. #FF5733 or #F53).",
                new[] { nameof(Color) });
    }
}

public class AmendTransactionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Effective date is required")]
    public DateTimeOffset EffectiveDate { get; set; }

    [Required(ErrorMessage = "Amount is required")]
    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Amount must be between -1,000,000,000 and 1,000,000,000")]
    public decimal Amount { get; set; }

    [MaxLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    public string? Description { get; set; }

    [MaxLength(100, ErrorMessage = "Category cannot exceed 100 characters")]
    public string? Category { get; set; }

    [MaxLength(7, ErrorMessage = "Color must be a valid hex value (e.g. #FF5733)")]
    public string? Color { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount == 0)
            yield return new ValidationResult(
                "Amount cannot be zero.", new[] { nameof(Amount) });

        var now = DateTimeOffset.UtcNow;
        if (EffectiveDate < now.AddYears(-50))
            yield return new ValidationResult(
                "Effective date cannot be more than 50 years in the past.",
                new[] { nameof(EffectiveDate) });

        if (EffectiveDate > now.AddYears(50))
            yield return new ValidationResult(
                "Effective date cannot be more than 50 years in the future.",
                new[] { nameof(EffectiveDate) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Description))
            yield return new ValidationResult(
                "Description contains invalid characters.", new[] { nameof(Description) });

        if (InputSanitizer.ContainsSqlInjectionPatterns(Category))
            yield return new ValidationResult(
                "Category contains invalid characters.", new[] { nameof(Category) });

        if (Color != null && InputSanitizer.SanitizeHexColor(Color) == null)
            yield return new ValidationResult(
                "Color must be a valid hex color (e.g. #FF5733 or #F53).",
                new[] { nameof(Color) });
    }
}

// ── Category DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Represents a user-defined transaction category. Categories are scoped to a
/// user (not a specific account), so one list covers all of a user's accounts.
/// </summary>
public class UserCategoryDto
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateCategoryRequest : IValidatableObject
{
    [Required(ErrorMessage = "Category name is required")]
    [MinLength(1, ErrorMessage = "Category name cannot be empty")]
    [MaxLength(100, ErrorMessage = "Category name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InputSanitizer.ContainsSqlInjectionPatterns(Name))
            yield return new ValidationResult(
                "Category name contains invalid characters.", new[] { nameof(Name) });
    }
}

// ── Projection DTOs ───────────────────────────────────────────────────────────

public class TransactionWithBalanceDto
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset StartDate { get; set; }
    public string? Category { get; set; }
    public decimal RunningBalance { get; set; }
    public decimal BalanceChange { get; set; }
    public string Type => Amount >= 0 ? "income" : "expense";
    public decimal AbsoluteAmount => Math.Abs(Amount);
    public string? Color { get; set; }
}

public class EnhancedCashFlowProjection
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset EndDate { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal NetChange { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public List<DailyBalanceSnapshot> DailySnapshots { get; set; } = new();
    public List<TransactionWithBalanceDto> Transactions { get; set; } = new();
}

public class DailyBalanceSnapshot
{
    public DateTime Date { get; set; }
    public decimal EndOfDayBalance { get; set; }
    public decimal StartOfDayBalance { get; set; }
    public decimal DayChange { get; set; }
    public int TransactionCount { get; set; }
    public decimal DayIncome { get; set; }
    public decimal DayExpenses { get; set; }
    public bool HasNegativeBalance { get; set; }
    public decimal LowestBalance { get; set; }
}

public class TransactionOccurrence
{
    public Guid TransactionId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime OccurrenceDate { get; set; }
    public FrequencyType Frequency { get; set; }
    public string? Color { get; set; }

    public string AmountDisplay => Amount.ToString("C");
    public string CategoryDisplay => string.IsNullOrEmpty(Category) ? "Uncategorized" : Category;
}

public class EnhancedMonthlyOverview
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid AccountId { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal NetChange { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal AverageDailyBalance { get; set; }
    public decimal HighestBalance { get; set; }
    public decimal LowestBalance { get; set; }
    public int DaysWithNegativeBalance { get; set; }
    public List<DailyBalanceSnapshot> DailyBreakdown { get; set; } = new();
    public List<CategoryBreakdown> CategoryBreakdowns { get; set; } = new();
}

public class CategoryBreakdown
{
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }
    public decimal PercentageOfTotal { get; set; }
}