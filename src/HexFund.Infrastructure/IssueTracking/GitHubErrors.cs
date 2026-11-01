using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using HexFund.Application.Dispatching;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>Turns GitHub responses into <see cref="IssueTrackerException"/>s with a safe message.</summary>
internal static class GitHubErrors
{
    /// <summary>
    /// Outages, rate limits and credential or repository problems are transient: the first two clear
    /// by themselves and the rest clear when the maintainer fixes the configuration, after which
    /// pending reports flow again. Other client errors (for example a rejected payload) would never
    /// succeed on retry, so they are permanent.
    /// </summary>
    public static IssueTrackerException FromStatus(HttpStatusCode status, HttpResponseHeaders headers, DateTimeOffset now)
    {
        var code = (int)status;
        var retryAfter = ParseRetryAfter(headers, now);
        var message = $"GitHub returned {code}";

        var transient = code == 429
            || code >= 500
            || (code == 403 && retryAfter.HasValue)
            || code is 401 or 403 or 404;

        return new IssueTrackerException(message, transient, retryAfter);
    }

    public static IssueTrackerException Network(Exception inner) =>
        new("Network error calling GitHub", isTransient: true, inner: inner);

    public static IssueTrackerException Timeout(Exception inner) =>
        new("GitHub request timed out", isTransient: true, inner: inner);

    private static TimeSpan? ParseRetryAfter(HttpResponseHeaders headers, DateTimeOffset now)
    {
        if (headers.RetryAfter?.Delta is { } delta)
            return delta;

        var exhausted = headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.FirstOrDefault() == "0";
        if (exhausted
            && headers.TryGetValues("x-ratelimit-reset", out var reset)
            && long.TryParse(reset.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
        {
            var wait = DateTimeOffset.FromUnixTimeSeconds(epoch) - now;
            return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
        }

        return null;
    }
}
