using System.ComponentModel.DataAnnotations;
using HexFund.Application.Validation;
using HexFund.Core.Enums;

namespace HexFund.Application.DTOs;

/// <summary>A bug report, idea or question sent from the app.</summary>
public class SubmitFeedbackRequest : IValidatableObject
{
    private string _body = string.Empty;

    public required FeedbackType Type { get; set; }

    [Required(ErrorMessage = "Body is required")]
    [MinLength(10, ErrorMessage = "Body must be at least 10 characters")]
    [MaxLength(4000, ErrorMessage = "Body cannot exceed 4000 characters")]
    public string Body
    {
        get => _body;
        set => _body = InputSanitizer.NormalizeMultilineText(value) ?? string.Empty;
    }

    /// <summary>Null or absent when the user switched diagnostics off.</summary>
    public FeedbackDiagnosticsDto? Diagnostics { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Type))
            yield return new ValidationResult("Invalid feedback type.", new[] { nameof(Type) });

        if (Diagnostics?.RecentErrors?.Count > FeedbackRules.MaxRecentErrors)
            yield return new ValidationResult(
                $"At most {FeedbackRules.MaxRecentErrors} recent errors are accepted.",
                new[] { "Diagnostics.RecentErrors" });
    }
}

/// <summary>Device and app details attached to a report. Every field is optional.</summary>
public class FeedbackDiagnosticsDto
{
    private string? _appVersion;
    private string? _buildNumber;
    private string? _platform;
    private string? _osVersion;
    private string? _deviceModel;
    private string? _screen;
    private string? _theme;

    [MaxLength(20, ErrorMessage = "App version cannot exceed 20 characters")]
    public string? AppVersion { get => _appVersion; set => _appVersion = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(20, ErrorMessage = "Build number cannot exceed 20 characters")]
    public string? BuildNumber { get => _buildNumber; set => _buildNumber = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(20, ErrorMessage = "Platform cannot exceed 20 characters")]
    public string? Platform { get => _platform; set => _platform = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(30, ErrorMessage = "OS version cannot exceed 30 characters")]
    public string? OsVersion { get => _osVersion; set => _osVersion = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(80, ErrorMessage = "Device model cannot exceed 80 characters")]
    public string? DeviceModel { get => _deviceModel; set => _deviceModel = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(50, ErrorMessage = "Screen cannot exceed 50 characters")]
    public string? Screen { get => _screen; set => _screen = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(30, ErrorMessage = "Theme cannot exceed 30 characters")]
    public string? Theme { get => _theme; set => _theme = InputSanitizer.NormalizeOptionalText(value); }

    public List<FeedbackErrorEntryDto>? RecentErrors { get; set; }
}

/// <summary>One recent failed API call recorded by the client.</summary>
public class FeedbackErrorEntryDto
{
    private string? _method;
    private string? _path;
    private string? _error;
    private string? _correlationId;

    /// <summary>ISO 8601 timestamp. Kept as text so a malformed value drops the entry instead of failing the request.</summary>
    public string? At { get; set; }

    [MaxLength(10, ErrorMessage = "Method cannot exceed 10 characters")]
    public string? Method { get => _method; set => _method = InputSanitizer.NormalizeOptionalText(value); }

    /// <summary>A route template such as <c>accounts/{id}/monthly-overview</c>. Query strings and ids are removed.</summary>
    [MaxLength(200, ErrorMessage = "Path cannot exceed 200 characters")]
    public string? Path { get => _path; set => _path = FeedbackRules.CleanPath(value); }

    [Range(0, 599, ErrorMessage = "Status must be between 0 and 599")]
    public int? Status { get; set; }

    [MaxLength(100, ErrorMessage = "Error cannot exceed 100 characters")]
    public string? Error { get => _error; set => _error = InputSanitizer.NormalizeOptionalText(value); }

    [MaxLength(64, ErrorMessage = "Correlation id cannot exceed 64 characters")]
    public string? CorrelationId { get => _correlationId; set => _correlationId = InputSanitizer.NormalizeOptionalText(value); }
}

/// <summary>Returned when a report has been stored. The issue itself is created asynchronously.</summary>
public class FeedbackReceiptDto
{
    public Guid Id { get; set; }
}
