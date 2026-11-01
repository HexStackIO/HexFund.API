using System.Globalization;
using System.Text.Json;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Application.Validation;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Core.Interfaces;

namespace HexFund.Application.Services;

public class FeedbackService : IFeedbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IFeedbackRepository _feedbackRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFeedbackDispatchSignal _dispatchSignal;

    public FeedbackService(
        IFeedbackRepository feedbackRepository,
        IUserRepository userRepository,
        IFeedbackDispatchSignal dispatchSignal)
    {
        _feedbackRepository = feedbackRepository;
        _userRepository = userRepository;
        _dispatchSignal = dispatchSignal;
    }

    public async Task<FeedbackReceiptDto?> SubmitAsync(Guid userId, SubmitFeedbackRequest request)
    {
        // The foreign key would otherwise surface a user who has not synced as a 500.
        if (await _userRepository.GetByIdAsync(userId) == null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = request.Type,
            Body = request.Body,
            DiagnosticsJson = SerialiseDiagnostics(request.Diagnostics),
            OutboxStatus = FeedbackOutboxStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now
        };

        await _feedbackRepository.AddAsync(feedback);

        // Wake the dispatcher so the report goes out now rather than at the next poll.
        _dispatchSignal.Notify();

        return new FeedbackReceiptDto { Id = feedback.Id };
    }

    /// <summary>
    /// Drops error entries whose timestamp is not a valid date, rewrites the rest to a canonical UTC
    /// form, and serialises the result. Null stays null (diagnostics were switched off).
    /// </summary>
    private static string? SerialiseDiagnostics(FeedbackDiagnosticsDto? diagnostics)
    {
        if (diagnostics == null)
            return null;

        var errors = diagnostics.RecentErrors?
            .Select(e => (Entry: e, Time: FeedbackRules.ParseTimestamp(e.At)))
            .Where(x => x.Time.HasValue)
            .Select(x =>
            {
                x.Entry.At = x.Time!.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                return x.Entry;
            })
            .ToList();

        diagnostics.RecentErrors = errors;
        return JsonSerializer.Serialize(diagnostics, JsonOptions);
    }
}
