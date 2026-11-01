using System.ComponentModel.DataAnnotations;
using HexFund.Application.DTOs;
using HexFund.Application.Validation;
using HexFund.Core.Enums;

namespace HexFund.Tests.Validation;

public class FeedbackValidationTests
{
    /// <summary>Validates the way MVC does: the request, then nested diagnostics and error entries.</summary>
    private static List<ValidationResult> Validate(SubmitFeedbackRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        if (request.Diagnostics != null)
        {
            Validator.TryValidateObject(request.Diagnostics, new ValidationContext(request.Diagnostics), results, true);
            foreach (var entry in request.Diagnostics.RecentErrors ?? new())
                Validator.TryValidateObject(entry, new ValidationContext(entry), results, true);
        }

        return results;
    }

    private static SubmitFeedbackRequest Valid() => new() { Type = FeedbackType.Bug, Body = "The calendar shows rent on the wrong day." };

    [Fact]
    public void ValidRequest_Passes() => Assert.Empty(Validate(Valid()));

    [Theory]
    [InlineData("too short", false)]
    [InlineData("exactly 10", true)]
    public void Body_MinimumIsTenCharacters(string body, bool valid)
    {
        var request = Valid();
        request.Body = body;
        Assert.Equal(valid, !Validate(request).Any(r => r.MemberNames.Contains("Body")));
    }

    [Fact]
    public void Body_MaximumIs4000Characters()
    {
        var request = Valid();
        request.Body = new string('a', 4000);
        Assert.Empty(Validate(request));

        request.Body = new string('a', 4001);
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Body"));
    }

    [Fact]
    public void Body_KeepsNewlinesAndTabs_ButRemovesOtherControlCharacters()
    {
        var request = Valid();
        request.Body = "  line one\r\nline two\rline\tthree\u0000\u0007  ";

        Assert.Equal("line one\nline two\nline\tthree", request.Body);
    }

    [Fact]
    public void Body_OnlyControlCharacters_FailsRequired()
    {
        var request = Valid();
        request.Body = "\u0001\u0002   ";
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Body"));
    }

    [Fact]
    public void UnknownType_IsRejected()
    {
        var request = Valid();
        request.Type = (FeedbackType)9;
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Type"));
    }

    [Theory]
    [InlineData(nameof(FeedbackDiagnosticsDto.AppVersion), 20)]
    [InlineData(nameof(FeedbackDiagnosticsDto.BuildNumber), 20)]
    [InlineData(nameof(FeedbackDiagnosticsDto.Platform), 20)]
    [InlineData(nameof(FeedbackDiagnosticsDto.OsVersion), 30)]
    [InlineData(nameof(FeedbackDiagnosticsDto.DeviceModel), 80)]
    [InlineData(nameof(FeedbackDiagnosticsDto.Screen), 50)]
    [InlineData(nameof(FeedbackDiagnosticsDto.Theme), 30)]
    public void DiagnosticsStrings_EnforceTheirLimits(string property, int limit)
    {
        var diagnostics = new FeedbackDiagnosticsDto();
        var setter = typeof(FeedbackDiagnosticsDto).GetProperty(property)!;

        setter.SetValue(diagnostics, new string('x', limit));
        var request = Valid();
        request.Diagnostics = diagnostics;
        Assert.Empty(Validate(request));

        setter.SetValue(diagnostics, new string('x', limit + 1));
        Assert.Contains(Validate(request), r => r.MemberNames.Contains(property));
    }

    [Fact]
    public void ErrorEntry_PathIsReducedToATemplate()
    {
        var id = Guid.NewGuid();
        var entry = new FeedbackErrorEntryDto { Path = $"accounts/{id}/transactions/{id}?includeHistory=true#top" };

        Assert.Equal("accounts/{id}/transactions/{id}", entry.Path);
    }

    [Fact]
    public void ErrorEntry_KeepsNonGuidSegments()
    {
        Assert.Equal("accounts/{id}/monthly-overview", FeedbackRules.CleanPath("accounts/{id}/monthly-overview"));
        Assert.Equal("auth/sync", FeedbackRules.CleanPath("auth/sync"));
        Assert.Null(FeedbackRules.CleanPath("  "));
    }

    [Fact]
    public void ErrorEntry_FieldLimitsAndStatusRange()
    {
        var request = Valid();
        request.Diagnostics = new FeedbackDiagnosticsDto
        {
            RecentErrors = new()
            {
                new FeedbackErrorEntryDto { Method = new string('M', 11), Status = 700, Error = new string('e', 101), CorrelationId = new string('c', 65) }
            }
        };

        var members = Validate(request).SelectMany(r => r.MemberNames).ToList();

        Assert.Contains("Method", members);
        Assert.Contains("Status", members);
        Assert.Contains("Error", members);
        Assert.Contains("CorrelationId", members);
    }

    [Fact]
    public void MoreThanTwentyRecentErrors_IsRejected()
    {
        var request = Valid();
        request.Diagnostics = new FeedbackDiagnosticsDto
        {
            RecentErrors = Enumerable.Range(0, 21).Select(_ => new FeedbackErrorEntryDto()).ToList()
        };
        Assert.Contains(Validate(request), r => r.MemberNames.Contains("Diagnostics.RecentErrors"));

        request.Diagnostics.RecentErrors.RemoveAt(0);
        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("2026-10-20T14:01:22Z", true)]
    [InlineData("2026-10-20T10:01:22-04:00", true)]
    [InlineData("not a date", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParseTimestamp_AcceptsOnlyRealDates(string? value, bool valid) =>
        Assert.Equal(valid, FeedbackRules.ParseTimestamp(value).HasValue);
}
