using HexFund.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HexFund.Application.Dispatching;

/// <summary>Settings under the <c>Feedback</c> configuration section.</summary>
public sealed class FeedbackOptions
{
    public const string Section = "Feedback";

    /// <summary>How long delivered reports are kept, in months. Valid range 1 to 60.</summary>
    public int RetentionMonths { get; set; } = 12;
}

/// <summary>
/// Deletes delivered reports older than the retention period. Pending and failed reports are never
/// removed by age, and deleting a row never touches the issue that was created from it.
/// </summary>
public class FeedbackRetentionProcessor
{
    private readonly IFeedbackRepository _repository;
    private readonly TimeProvider _time;
    private readonly FeedbackOptions _options;
    private readonly ILogger<FeedbackRetentionProcessor> _logger;

    public FeedbackRetentionProcessor(
        IFeedbackRepository repository,
        TimeProvider time,
        IOptions<FeedbackOptions> options,
        ILogger<FeedbackRetentionProcessor> logger)
    {
        _repository = repository;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        var cutoff = _time.GetUtcNow().AddMonths(-_options.RetentionMonths);
        var deleted = await _repository.DeleteSentOlderThanAsync(cutoff);

        if (deleted > 0)
            _logger.LogInformation(
                "Feedback retention removed {Count} delivered report(s) older than {Months} month(s)",
                deleted, _options.RetentionMonths);

        return deleted;
    }
}
