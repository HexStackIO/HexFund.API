using HexFund.Application.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace HexFund.API.Middleware;

/// <summary>
/// Global action filter that:
///   1. Short-circuits with a consistent 400 response when ModelState is invalid,
///      so individual controllers do not need their own if (!ModelState.IsValid) guards.
///   2. Sanitizes all string properties on the incoming request object before the
///      action method executes, so service layer and DB always receive clean input.
///
/// Registration in Program.cs:
///   builder.Services.AddControllers(options =>
///       options.Filters.Add&lt;ValidationActionFilter&gt;());
/// </summary>
public class ValidationActionFilter : IActionFilter
{
    private readonly ILogger<ValidationActionFilter> _logger;

    public ValidationActionFilter(ILogger<ValidationActionFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // ── Step 1: sanitize string properties on every bound argument ────────
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument == null) continue;
            SanitizeObject(argument);
        }

        // ── Step 2: reject if ModelState invalid after sanitization ───────────
        if (!context.ModelState.IsValid)
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            _logger.LogWarning(
                "Validation failed for {Action}: {Errors}",
                context.ActionDescriptor.DisplayName,
                JsonSerializer.Serialize(errors));

            context.Result = new BadRequestObjectResult(new ValidationErrorResponse
            {
                Message = "One or more validation errors occurred.",
                Errors  = errors
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    /// <summary>
    /// Walks the public string properties of an object and runs them through
    /// InputSanitizer.SanitizeText in-place. Does not recurse into nested objects
    /// (the DTO layer is shallow) but handles nullable strings safely.
    /// </summary>
    private static void SanitizeObject(object obj)
    {
        var type = obj.GetType();

        // Skip primitives, value types, and strings themselves
        if (type.IsPrimitive || type.IsValueType || type == typeof(string))
            return;

        foreach (var prop in type.GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.PropertyType != typeof(string)) continue;

            var raw = prop.GetValue(obj) as string;
            if (raw == null) continue;

            var sanitized = InputSanitizer.SanitizeText(raw);
            prop.SetValue(obj, sanitized);
        }
    }
}

/// <summary>
/// Consistent shape for all 400 validation error responses.
/// </summary>
public class ValidationErrorResponse
{
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Key = field name (camelCase matches the DTO property name),
    /// Value = array of human-readable error messages for that field.
    /// </summary>
    public Dictionary<string, string[]> Errors { get; set; } = new();
}
