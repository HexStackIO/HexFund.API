using System.Text.RegularExpressions;

namespace HexFund.Application.Validation;

/// <summary>
/// Input normalisation applied by DTO property setters, so validation attributes
/// always see the cleaned value.
///
/// Free text is trimmed and stripped of control characters, nothing more: the API
/// returns JSON and EF Core parameterises every query, so HTML/SQL "scrubbing" would
/// only corrupt legitimate text (e.g. "Rent -- March", "Metadata: x"). Escaping is the
/// responsibility of whatever renders the value.
/// </summary>
public static class InputSanitizer
{
    // ASCII control characters except tab (0x09), LF (0x0A) and CR (0x0D)
    private static readonly Regex ControlChars =
        new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    // #RGB or #RRGGBB
    private static readonly Regex HexColor =
        new(@"^#([0-9A-F]{3}|[0-9A-F]{6})$", RegexOptions.Compiled);

    /// <summary>Strips control characters and trims. Null stays null.</summary>
    public static string? NormalizeText(string? input) =>
        input == null ? null : ControlChars.Replace(input, string.Empty).Trim();

    /// <summary>
    /// For multi-line text such as feedback. Strips control characters other than tab and newline,
    /// converts CRLF and lone CR to LF, and trims. Null stays null.
    /// </summary>
    public static string? NormalizeMultilineText(string? input) =>
        input == null
            ? null
            : ControlChars.Replace(input, string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim();

    /// <summary>Like <see cref="NormalizeText"/> but maps blank input to null.</summary>
    public static string? NormalizeOptionalText(string? input)
    {
        var text = NormalizeText(input);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Uppercases and trims a colour; blank becomes null. Validity is checked separately.</summary>
    public static string? NormalizeColor(string? input)
    {
        var text = NormalizeOptionalText(input);
        return text?.ToUpperInvariant();
    }

    /// <summary>True for #RGB / #RRGGBB (expects an already-normalised value).</summary>
    public static bool IsValidHexColor(string? input) =>
        input != null && HexColor.IsMatch(input);
}
