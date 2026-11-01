using HexFund.Application.DTOs;
using HexFund.Application.Issues;

namespace HexFund.Application.Interfaces;

public interface IFeedbackService
{
    /// <summary>
    /// Stores the report as pending and returns its id. Creating the issue happens later, in the
    /// background. Returns null when the user has no local record yet (they have not synced).
    /// </summary>
    Task<FeedbackReceiptDto?> SubmitAsync(Guid userId, SubmitFeedbackRequest request);
}

/// <summary>The outbound side of feedback: creating issues in the private tracker repository.</summary>
public interface IIssueTracker
{
    /// <summary>Creates the issue and returns its number.</summary>
    Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken);

    /// <summary>
    /// Looks for an existing issue whose body contains <paramref name="marker"/>, searching issues
    /// created on or after <paramref name="createdSince"/>. Used before a retry so an issue that was
    /// created but not acknowledged is adopted rather than duplicated.
    /// </summary>
    Task<int?> FindIssueByMarkerAsync(string marker, DateTimeOffset createdSince, CancellationToken cancellationToken);
}
