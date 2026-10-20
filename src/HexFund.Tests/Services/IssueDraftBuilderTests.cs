using HexFund.Application.Issues;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Services;

public class IssueDraftBuilderTests
{
    private static Feedback Report(string body, FeedbackType type = FeedbackType.Bug, string? diagnosticsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Type = type,
        Body = body,
        DiagnosticsJson = diagnosticsJson,
        CreatedAt = Utc(2026, 10, 20, 14, 5)
    };

    [Theory]
    [InlineData(FeedbackType.Bug, "[Bug] ", "bug")]
    [InlineData(FeedbackType.Idea, "[Idea] ", "feature")]
    [InlineData(FeedbackType.Question, "[Question] ", "question")]
    public void TitleAndLabels_FollowTheType(FeedbackType type, string prefix, string typeLabel)
    {
        var draft = IssueDraftBuilder.Build(Report("Something is broken here", type));

        Assert.StartsWith(prefix, draft.Title);
        Assert.Equal(new[] { "needs-triage", "source-app", typeLabel }, draft.Labels);
    }

    [Fact]
    public void Title_UsesTheFirstNonEmptyLineWithCollapsedWhitespace()
    {
        var draft = IssueDraftBuilder.Build(Report("\n\n   rent    shows \t wrong day\nsecond line"));
        Assert.Equal("[Bug] rent shows wrong day", draft.Title);
    }

    [Fact]
    public void Title_IsTruncatedToSeventyCharactersOfSummary()
    {
        var draft = IssueDraftBuilder.Build(Report(new string('a', 200)));

        var summary = draft.Title["[Bug] ".Length..];
        Assert.Equal(70, summary.Length);
        Assert.EndsWith("...", summary);
    }

    [Fact]
    public void Title_NeutralisesMentionsReferencesAndBackticks()
    {
        var draft = IssueDraftBuilder.Build(Report("@octocat please see #123 and org/repo#9 `code`"));

        Assert.DoesNotContain('@', draft.Title);
        Assert.DoesNotContain('#', draft.Title);
        Assert.DoesNotContain('`', draft.Title);
    }

    [Fact]
    public void Body_StartsWithTheMarker_AndShowsTimeAndType()
    {
        var report = Report("Calendar bug text", FeedbackType.Idea);
        var body = IssueDraftBuilder.Build(report).Body;

        Assert.StartsWith($"<!-- {IssueDraftBuilder.MarkerFor(report.Id)} -->\n", body);
        Assert.Contains("**Submitted:** 2026-10-20 14:05 UTC", body);
        Assert.Contains("**Type:** Idea", body);
    }

    [Fact]
    public void Report_IsInsideAFenceLongerThanAnyBacktickRun()
    {
        var text = "before ````` five ticks ``` three\nafter";
        var body = IssueDraftBuilder.Build(Report(text)).Body;

        var fence = new string('`', 6);
        Assert.Contains($"{fence}\n{text}\n{fence}\n", body);
    }

    [Fact]
    public void Report_UsesAtLeastATripleBacktickFence()
    {
        var body = IssueDraftBuilder.Build(Report("plain text report")).Body;
        Assert.Contains("```\nplain text report\n```\n", body);
    }

    [Fact]
    public void MentionsInTheReport_AppearOnlyInsideTheFence()
    {
        var body = IssueDraftBuilder.Build(Report("hello @everyone and @octocat, see #12")).Body;

        var outside = RemoveFencedBlock(body);
        Assert.DoesNotContain("@everyone", outside);
        Assert.DoesNotContain("@octocat", outside);
        Assert.DoesNotContain("#12", outside);
    }

    [Fact]
    public void Body_NeverContainsTheUserId()
    {
        var report = Report("A report from someone", diagnosticsJson: """{"appVersion":"1.9","platform":"Android"}""");
        var draft = IssueDraftBuilder.Build(report);

        Assert.DoesNotContain(report.UserId.ToString(), draft.Title + draft.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_AreListedAsCodeSpans()
    {
        var json = """
            {"appVersion":"1.9","buildNumber":"23","platform":"Android","osVersion":"16",
             "deviceModel":"Google Pixel 8","screen":"calendar","theme":"Gold"}
            """;
        var body = IssueDraftBuilder.Build(Report("Report text here", diagnosticsJson: json)).Body;

        Assert.Contains("- **App version:** ` 1.9 `", body);
        Assert.Contains("- **Device:** ` Google Pixel 8 `", body);
        Assert.Contains("- **Theme:** ` Gold `", body);
    }

    [Fact]
    public void NoDiagnostics_IsStatedExplicitly()
    {
        var body = IssueDraftBuilder.Build(Report("Report text here")).Body;
        Assert.Contains("_Not provided._", body);
        Assert.DoesNotContain("Recent errors", body);
    }

    [Fact]
    public void RecentErrors_RenderAsATable_WithPipesAndBackticksContained()
    {
        var json = """
            {"recentErrors":[
              {"at":"2026-10-20T14:01:22Z","method":"GET","path":"accounts/{id}/monthly-overview",
               "status":500,"error":"bad | pipe and ` tick","correlationId":"b1c7aaaa-1111"}]}
            """;
        var body = IssueDraftBuilder.Build(Report("Report text here", diagnosticsJson: json)).Body;

        Assert.Contains("| Time (UTC) | Request | Status | Error | Correlation id |", body);
        Assert.Contains("2026-10-20 14:01:22", body);
        Assert.Contains("GET accounts/{id}/monthly-overview", body);
        Assert.Contains("500", body);
        Assert.Contains(@"`` bad \| pipe and ` tick ``", body);
    }

    [Fact]
    public void MalformedDiagnosticsJson_IsTreatedAsMissing()
    {
        var body = IssueDraftBuilder.Build(Report("Report text here", diagnosticsJson: "{not json")).Body;
        Assert.Contains("_Not provided._", body);
    }

    private static string RemoveFencedBlock(string body)
    {
        var start = body.IndexOf("```", StringComparison.Ordinal);
        var end = body.LastIndexOf("```", StringComparison.Ordinal);
        return start < 0 || end <= start ? body : body.Remove(start, end - start + 3);
    }
}
