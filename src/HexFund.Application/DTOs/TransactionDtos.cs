using System.ComponentModel.DataAnnotations;
using HexFund.Application.Validation;
using HexFund.Core.Enums;

namespace HexFund.Application.DTOs;

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

/// <summary>Fields and rules shared by create, update and amend requests.</summary>
public abstract class TransactionRequestBase : IValidatableObject
{
    private string? _category;
    private string? _color;

    [Range(-1_000_000_000, 1_000_000_000,
        ErrorMessage = "Amount must be between -1,000,000,000 and 1,000,000,000")]
    public required decimal Amount { get; set; }

    [MaxLength(100, ErrorMessage = "Category cannot exceed 100 characters")]
    public string? Category
    {
        get => _category;
        set => _category = InputSanitizer.NormalizeOptionalText(value);
    }

    [MaxLength(7, ErrorMessage = "Color must be a valid hex value (e.g. #FF5733)")]
    public string? Color
    {
        get => _color;
        set => _color = InputSanitizer.NormalizeColor(value);
    }

    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount == 0)
            yield return new ValidationResult("Amount cannot be zero.", new[] { nameof(Amount) });

        if (decimal.Round(Amount, 2) != Amount)
            yield return new ValidationResult(
                "Amount cannot have more than 2 decimal places.", new[] { nameof(Amount) });

        if (Color != null && !InputSanitizer.IsValidHexColor(Color))
            yield return new ValidationResult(
                "Color must be a valid hex color (e.g. #FF5733 or #F53).", new[] { nameof(Color) });
    }
}

/// <summary>Fields shared by create and update (a full replacement of the transaction).</summary>
public abstract class TransactionDetailsRequest : TransactionRequestBase
{
    private string _description = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    [MaxLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    public string Description
    {
        get => _description;
        set => _description = InputSanitizer.NormalizeText(value) ?? string.Empty;
    }

    public required FrequencyType Frequency { get; set; }

    public required DateTimeOffset StartDate { get; set; }

    public DateTimeOffset? EndDate { get; set; }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        if (!Enum.IsDefined(Frequency))
            yield return new ValidationResult("Invalid frequency value.", new[] { nameof(Frequency) });
    }
}

[ValidDateRange]
public class CreateTransactionRequest : TransactionDetailsRequest
{
}

[ValidDateRange]
public class UpdateTransactionRequest : TransactionDetailsRequest
{
    public required bool IsActive { get; set; }
}

/// <summary>
/// Applies new values to a recurring transaction from <see cref="EffectiveDate"/> forward.
/// Omitted description/category/color are inherited from the original.
/// </summary>
public class AmendTransactionRequest : TransactionRequestBase
{
    private string? _description;

    public required DateTimeOffset EffectiveDate { get; set; }

    [MaxLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    public string? Description
    {
        get => _description;
        set => _description = InputSanitizer.NormalizeOptionalText(value);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        var now = DateTimeOffset.UtcNow;
        if (EffectiveDate < now.AddYears(-50) || EffectiveDate > now.AddYears(50))
            yield return new ValidationResult(
                "Effective date must be within 50 years of today.", new[] { nameof(EffectiveDate) });
    }
}
