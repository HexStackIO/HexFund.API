using System.Text.RegularExpressions;

namespace HexFund.Application.Validation;

/// <summary>
/// Central sanitization utilities applied before any value reaches the database.
///
/// Design philosophy:
///   - Free-text fields (Description, Category, AccountName) are stripped of HTML/script
///     and control characters, but otherwise preserved. We do NOT silently truncate —
///     oversized inputs are rejected by the validator so the caller knows exactly what failed.
///   - Structured fields (Color hex, Currency code) are validated by format, not sanitized.
///   - We never return null from a sanitize call — always return string.Empty as the floor.
/// </summary>
public static class InputSanitizer
{
    // Matches any HTML/XML tag, e.g. <script>, </div>, <img src=...>
    private static readonly Regex HtmlTagPattern =
        new(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches javascript: and data: URI schemes that can embed scripts
    private static readonly Regex DangerousUriPattern =
        new(@"(javascript|data|vbscript)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches SQL comment sequences and common injection tokens
    private static readonly Regex SqlInjectionPattern =
        new(@"(-{2}|\/\*|\*\/|;\s*(drop|delete|insert|update|select|exec|xp_)\s)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches ASCII control characters (0x00-0x1F) except tab (0x09), LF (0x0A), CR (0x0D)
    private static readonly Regex ControlCharPattern =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes a free-text field (description, category, account name).
    /// Strips HTML tags, dangerous URI schemes, and control characters.
    /// Trims surrounding whitespace.
    /// </summary>
    public static string SanitizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var result = input.Trim();
        result = HtmlTagPattern.Replace(result, string.Empty);
        result = DangerousUriPattern.Replace(result, string.Empty);
        result = ControlCharPattern.Replace(result, string.Empty);

        // Collapse any runs of whitespace left behind by removals to a single space
        result = Regex.Replace(result, @"\s{2,}", " ").Trim();

        return result;
    }

    /// <summary>
    /// Returns true if the input contains patterns that look like SQL injection attempts.
    /// Used as an extra signal in validators — EF Core parameterization already prevents
    /// actual injection, but we want to log and reject obviously malicious inputs.
    /// </summary>
    public static bool ContainsSqlInjectionPatterns(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return SqlInjectionPattern.IsMatch(input);
    }

    /// <summary>
    /// Validates a hex color string. Accepts #RGB and #RRGGBB formats only.
    /// Returns the uppercased canonical form, or null if invalid/empty.
    /// </summary>
    public static string? SanitizeHexColor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim().ToUpperInvariant();

        // Accept #RGB (shorthand) or #RRGGBB
        if (Regex.IsMatch(trimmed, @"^#([0-9A-F]{3}|[0-9A-F]{6})$"))
            return trimmed;

        return null; // invalid format — caller's validator will reject
    }
}
