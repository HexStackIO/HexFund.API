using HexFund.Application.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>
/// Runs the feedback outbox in the background: a pass at startup, then another whenever a report is
/// submitted or the poll interval elapses, plus a daily retention sweep.
/// </summary>
/// <remarks>
/// The deployment is a single instance today, and the free App Service tier unloads the app when it
/// is idle, so this only runs while the app is awake. Rows are claimed atomically with a lease, which
/// keeps two overlapping instances (for example during a slot swap) from sending the same report.
/// Reports left pending are picked up at the next startup or request.
/// </remarks>
public sealed class FeedbackDispatcherHost : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan BacklogWarningAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan BacklogWarningInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly FeedbackDispatchSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<FeedbackDispatcherHost> _logger;

    private DateTimeOffset _lastRetention = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBacklogWarning = DateTimeOffset.MinValue;

    public FeedbackDispatcherHost(
        IServiceScopeFactory scopes,
        FeedbackDispatchSignal signal,
        TimeProvider time,
        ILogger<FeedbackDispatcherHost> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPassAsync(stoppingToken);

            try
            {
                await _signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<FeedbackDispatchProcessor>();

            await dispatcher.DrainAsync(stoppingToken);
            await WarnIfBackloggedAsync(dispatcher);

            if (_time.GetUtcNow() - _lastRetention >= RetentionInterval)
            {
                await scope.ServiceProvider.GetRequiredService<FeedbackRetentionProcessor>().RunAsync();
                _lastRetention = _time.GetUtcNow();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Typically the database being unreachable. Keep running; the next pass retries.
            _logger.LogError(ex, "Feedback dispatch pass failed");
        }
    }

    private async Task WarnIfBackloggedAsync(FeedbackDispatchProcessor dispatcher)
    {
        var now = _time.GetUtcNow();
        if (now - _lastBacklogWarning < BacklogWarningInterval)
            return;

        var age = await dispatcher.GetOldestPendingAgeAsync();
        if (age >= BacklogWarningAge)
        {
            _lastBacklogWarning = now;
            _logger.LogWarning(
                "The oldest pending feedback report has been waiting {Age}. Check the GitHub integration",
                age);
        }
    }
}
