using System.Globalization;

namespace HexFund.Application.Validation;

/// <summary>Rules applied to the diagnostics a client attaches to a feedback report.</summary>
public static class FeedbackRules
{
    public const int MaxRecentErrors = 20;

    /// <summary>
    /// Reduces an API path to something safe to store and publish: the query string and fragment are
    /// cut off and GUID-shaped segments become <c>{id}</c>. The client is expected to send templates
    /// already; this is the server-side backstop.
    /// </summary>
    public static string? CleanPath(string? path)
    {
        var normalized = InputSanitizer.NormalizeOptionalText(path);
        if (normalized == null)
            return null;

        var end = normalized.IndexOfAny(new[] { '?', '#' });
        if (end >= 0)
            normalized = normalized[..end];

        var segments = normalized.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (Guid.TryParse(segments[i], out _))
                segments[i] = "{id}";
        }

        return string.Join('/', segments);
    }

    /// <summary>Parses a client timestamp to UTC. Returns null when it is not a valid date and time.</summary>
    public static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
