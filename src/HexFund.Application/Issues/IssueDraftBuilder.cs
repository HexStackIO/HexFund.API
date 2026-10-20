using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HexFund.Application.DTOs;
using HexFund.Application.Validation;
using HexFund.Core.Entities;
using HexFund.Core.Enums;

namespace HexFund.Application.Issues;

/// <summary>
/// Turns a stored report into the title, body and labels of a GitHub issue. Pure and free of I/O.
/// </summary>
/// <remarks>
/// Everything that came from the client is treated as hostile text: the report sits inside a code
/// fence longer than any backtick run it contains, diagnostics values are rendered as code spans, and
/// the title has mentions and issue references neutralised. The user id is never read, so it cannot
/// leak into an issue.
/// </remarks>
public static partial class IssueDraftBuilder
{
    private const int MaxTitleSummaryLength = 70;
    private const string Ellipsis = "...";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The text that identifies a report inside its issue body.</summary>
    public static string MarkerFor(Guid feedbackId) => $"hexfund-feedback:{feedbackId}";

    public static IssueDraft Build(Feedback feedback)
    {
        var typeName = TypeName(feedback.Type);
        var title = $"[{typeName}] {Summarise(feedback.Body)}";

        var labels = new[] { "needs-triage", "source-app", TypeLabel(feedback.Type) };

        return new IssueDraft(title, BuildBody(feedback, typeName), labels);
    }

    private static string BuildBody(Feedback feedback, string typeName)
    {
        var body = new StringBuilder();
        body.Append("<!-- ").Append(MarkerFor(feedback.Id)).Append(" -->\n");
        body.Append("**Submitted:** ")
            .Append(feedback.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" UTC\n");
        body.Append("**Type:** ").Append(typeName).Append("\n\n");

        var fence = new string('`', Math.Max(3, LongestRun(feedback.Body, '`') + 1));
        body.Append("### Report\n")
            .Append(fence).Append('\n')
            .Append(feedback.Body).Append('\n')
            .Append(fence).Append("\n\n");

        body.Append("### Diagnostics\n");
        var diagnostics = ParseDiagnostics(feedback.DiagnosticsJson);
        if (diagnostics == null)
        {
            body.Append("_Not provided._\n");
            return body.ToString();
        }

        AppendDiagnostics(body, diagnostics);
        return body.ToString();
    }

    private static void AppendDiagnostics(StringBuilder body, FeedbackDiagnosticsDto d)
    {
        AppendItem(body, "App version", d.AppVersion);
        AppendItem(body, "Build", d.BuildNumber);
        AppendItem(body, "Platform", d.Platform);
        AppendItem(body, "OS version", d.OsVersion);
        AppendItem(body, "Device", d.DeviceModel);
        AppendItem(body, "Screen", d.Screen);
        AppendItem(body, "Theme", d.Theme);

        if (d.RecentErrors is not { Count: > 0 })
            return;

        body.Append("\n### Recent errors\n");
        body.Append("| Time (UTC) | Request | Status | Error | Correlation id |\n");
        body.Append("| --- | --- | --- | --- | --- |\n");

        foreach (var entry in d.RecentErrors)
        {
            var time = FeedbackRules.ParseTimestamp(entry.At)?
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var request = string.Join(' ', new[] { entry.Method, entry.Path }.Where(s => !string.IsNullOrEmpty(s)));

            body.Append("| ")
                .Append(Cell(time))
                .Append(" | ").Append(Cell(request))
                .Append(" | ").Append(Cell(entry.Status?.ToString(CultureInfo.InvariantCulture)))
                .Append(" | ").Append(Cell(entry.Error))
                .Append(" | ").Append(Cell(entry.CorrelationId))
                .Append(" |\n");
        }
    }

    private static void AppendItem(StringBuilder body, string label, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            body.Append("- **").Append(label).Append(":** ").Append(CodeSpan(value)).Append('\n');
    }

    /// <summary>A table cell holding a code span, with pipes escaped so the value cannot add columns.</summary>
    private static string Cell(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : CodeSpan(value).Replace("|", "\\|");

    /// <summary>Renders text as inline code that cannot be closed early by backticks in the text.</summary>
    private static string CodeSpan(string value)
    {
        var flat = WhitespaceRuns().Replace(value, " ").Trim();
        var ticks = new string('`', LongestRun(flat, '`') + 1);
        return $"{ticks} {flat} {ticks}";
    }

    private static string Summarise(string body)
    {
        var firstLine = body
            .Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;

        var summary = WhitespaceRuns().Replace(firstLine, " ").Trim();

        // Full-width look-alikes keep the text readable but stop it becoming a mention or a reference.
        summary = summary.Replace("`", string.Empty).Replace('@', '＠').Replace('#', '＃');

        if (summary.Length > MaxTitleSummaryLength)
            summary = summary[..(MaxTitleSummaryLength - Ellipsis.Length)].TrimEnd() + Ellipsis;

        return summary;
    }

    private static FeedbackDiagnosticsDto? ParseDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FeedbackDiagnosticsDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int LongestRun(string text, char c)
    {
        int longest = 0, current = 0;
        foreach (var ch in text)
        {
            current = ch == c ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static string TypeName(FeedbackType type) => type switch
    {
        FeedbackType.Bug => "Bug",
        FeedbackType.Idea => "Idea",
        _ => "Question"
    };

    private static string TypeLabel(FeedbackType type) => type switch
    {
        FeedbackType.Bug => "bug",
        FeedbackType.Idea => "feature",
        _ => "question"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();
}
