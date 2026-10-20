using System.Text.Json;
using HexFund.Application.DTOs;
using HexFund.Application.Services;
using HexFund.Core.Entities;
using HexFund.Core.Enums;

namespace HexFund.Tests.Services;

public class FeedbackServiceTests
{
    private readonly FakeFeedbackRepository _feedback = new();
    private readonly FakeUserRepository _users = new();
    private readonly FeedbackService _service;
    private readonly User _user = new() { UserId = Guid.NewGuid(), Email = "a@example.com" };

    public FeedbackServiceTests()
    {
        _users.Users.Add(_user);
        _service = new FeedbackService(_feedback, _users);
    }

    private static SubmitFeedbackRequest Request(FeedbackDiagnosticsDto? diagnostics = null) => new()
    {
        Type = FeedbackType.Idea,
        Body = "It would help to see next month at a glance.",
        Diagnostics = diagnostics
    };

    [Fact]
    public async Task Submit_StoresAPendingRowAndReturnsItsId()
    {
        var receipt = await _service.SubmitAsync(_user.UserId, Request());

        var row = Assert.Single(_feedback.Rows);
        Assert.Equal(receipt!.Id, row.Id);
        Assert.Equal(_user.UserId, row.UserId);
        Assert.Equal(FeedbackType.Idea, row.Type);
        Assert.Equal(FeedbackOutboxStatus.Pending, row.OutboxStatus);
        Assert.Equal(0, row.Attempts);
        Assert.True(row.NextAttemptAt <= DateTimeOffset.UtcNow);
        Assert.Null(row.DiagnosticsJson);
    }

    [Fact]
    public async Task Submit_ForAnUnsyncedUser_ReturnsNullAndStoresNothing()
    {
        var receipt = await _service.SubmitAsync(Guid.NewGuid(), Request());

        Assert.Null(receipt);
        Assert.Empty(_feedback.Rows);
    }

    [Fact]
    public async Task Submit_SerialisesDiagnostics_DroppingEntriesWithBadTimestamps()
    {
        var diagnostics = new FeedbackDiagnosticsDto
        {
            AppVersion = "1.9",
            RecentErrors = new()
            {
                new FeedbackErrorEntryDto { At = "2026-10-20T10:01:22-04:00", Method = "GET", Path = "auth/sync", Status = 500 },
                new FeedbackErrorEntryDto { At = "yesterday-ish", Method = "GET", Path = "auth/me", Status = 500 }
            }
        };

        await _service.SubmitAsync(_user.UserId, Request(diagnostics));

        using var json = JsonDocument.Parse(_feedback.Rows.Single().DiagnosticsJson!);
        Assert.Equal("1.9", json.RootElement.GetProperty("appVersion").GetString());

        var errors = json.RootElement.GetProperty("recentErrors");
        var kept = Assert.Single(errors.EnumerateArray());
        Assert.Equal("2026-10-20T14:01:22Z", kept.GetProperty("at").GetString());
        Assert.Equal("auth/sync", kept.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Submit_OmitsNullDiagnosticFieldsFromTheJson()
    {
        await _service.SubmitAsync(_user.UserId, Request(new FeedbackDiagnosticsDto { Platform = "Android" }));

        using var json = JsonDocument.Parse(_feedback.Rows.Single().DiagnosticsJson!);
        Assert.Equal("Android", json.RootElement.GetProperty("platform").GetString());
        Assert.False(json.RootElement.TryGetProperty("deviceModel", out _));
    }
}
