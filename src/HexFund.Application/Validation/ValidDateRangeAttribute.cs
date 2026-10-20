using System.ComponentModel.DataAnnotations;

namespace HexFund.Application.Validation;

/// <summary>
/// Class-level validation for request DTOs that carry a StartDate and an optional EndDate.
///
///   1. StartDate must be within 50 years of now (past or future).
///   2. If EndDate is present, it must be strictly after StartDate.
///   3. EndDate must not be more than 100 years after StartDate.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ValidDateRangeAttribute : ValidationAttribute
{
    private const int MaxPastYears = 50;
    private const int MaxFutureYears = 50;
    private const int MaxRangeYears = 100;

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success;

        var startProp = context.ObjectType.GetProperty("StartDate");
        var endProp = context.ObjectType.GetProperty("EndDate");

        if (startProp?.GetValue(value) is not DateTimeOffset start)
            return ValidationResult.Success;

        var now = DateTimeOffset.UtcNow;

        if (start < now.AddYears(-MaxPastYears))
            return new ValidationResult(
                $"Start date cannot be more than {MaxPastYears} years in the past.",
                new[] { "StartDate" });

        if (start > now.AddYears(MaxFutureYears))
            return new ValidationResult(
                $"Start date cannot be more than {MaxFutureYears} years in the future.",
                new[] { "StartDate" });

        if (endProp?.GetValue(value) is DateTimeOffset end)
        {
            if (end <= start)
                return new ValidationResult(
                    "End date must be after the start date.", new[] { "EndDate" });

            if (end > start.AddYears(MaxRangeYears))
                return new ValidationResult(
                    $"End date cannot be more than {MaxRangeYears} years after the start date.",
                    new[] { "EndDate" });
        }

        return ValidationResult.Success;
    }
}
