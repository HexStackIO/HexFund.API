using System.ComponentModel.DataAnnotations;

namespace HexFund.Application.Validation;

/// <summary>
/// Class-level validation attribute applied to any request DTO that carries
/// both a StartDate and an optional EndDate.
///
/// Rules enforced:
///   1. StartDate must not be more than 50 years in the past (sanity floor).
///   2. StartDate must not be more than 50 years in the future (sanity ceiling).
///   3. If EndDate is present, it must be strictly after StartDate.
///   4. EndDate must not be more than 100 years after StartDate.
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

        // Resolve StartDate — support both DateTimeOffset and DateTime properties
        var startProp = context.ObjectType.GetProperty("StartDate");
        var endProp = context.ObjectType.GetProperty("EndDate");

        if (startProp == null)
            return ValidationResult.Success; // attribute applied to a type without StartDate — skip

        var startRaw = startProp.GetValue(value);
        var endRaw = endProp?.GetValue(value);

        var startDate = startRaw switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => dt.ToUniversalTime(),
            _ => (DateTime?)null
        };

        if (startDate == null)
            return ValidationResult.Success;

        var now = DateTime.UtcNow;

        if (startDate < now.AddYears(-MaxPastYears))
            return new ValidationResult(
                $"Start date cannot be more than {MaxPastYears} years in the past.",
                new[] { "StartDate" });

        if (startDate > now.AddYears(MaxFutureYears))
            return new ValidationResult(
                $"Start date cannot be more than {MaxFutureYears} years in the future.",
                new[] { "StartDate" });

        if (endRaw != null)
        {
            var endDate = endRaw switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt.ToUniversalTime(),
                _ => (DateTime?)null
            };

            if (endDate != null)
            {
                if (endDate <= startDate)
                    return new ValidationResult(
                        "End date must be after the start date.",
                        new[] { "EndDate" });

                if (endDate > startDate.Value.AddYears(MaxRangeYears))
                    return new ValidationResult(
                        $"End date cannot be more than {MaxRangeYears} years after the start date.",
                        new[] { "EndDate" });
            }
        }

        return ValidationResult.Success;
    }
}