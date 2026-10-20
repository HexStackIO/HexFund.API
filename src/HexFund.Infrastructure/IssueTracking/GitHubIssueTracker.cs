using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexFund.Application.Dispatching;
using HexFund.Application.Interfaces;
using HexFund.Application.Issues;
using Microsoft.Extensions.Options;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>Creates and finds issues in the private tracker repository through the GitHub REST API.</summary>
public sealed class GitHubIssueTracker : IIssueTracker
{
    private const string TrackerLabel = "source-app";
    private const int PageSize = 100;
    private const int MaxPages = 5;

    private readonly HttpClient _http;
    private readonly GitHubAppTokenProvider _tokens;
    private readonly TimeProvider _time;
    private readonly string _repo;

    public GitHubIssueTracker(
        HttpClient http,
        GitHubAppTokenProvider tokens,
        IOptions<GitHubOptions> options,
        TimeProvider time)
    {
        _http = http;
        _tokens = tokens;
        _time = time;
        _repo = options.Value.TrackerRepo!;
    }

    public async Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken)
    {
        using var request = await NewRequestAsync(HttpMethod.Post, $"repos/{_repo}/issues", cancellationToken);
        request.Content = JsonContent.Create(new { title = draft.Title, body = draft.Body, labels = draft.Labels });

        using var response = await SendAsync(request, cancellationToken);
        var created = await ReadAsync<IssueResponse>(response, cancellationToken);
        return created.Number;
    }

    /// <summary>
    /// Scans recent issues carrying the tracker label for one whose body contains the marker. Listing
    /// reads the stored body directly, unlike search, which is eventually consistent and may not index
    /// a freshly created issue.
    /// </summary>
    public async Task<int?> FindIssueByMarkerAsync(
        string marker, DateTimeOffset createdSince, CancellationToken cancellationToken)
    {
        var since = Uri.EscapeDataString(createdSince.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"repos/{_repo}/issues?labels={TrackerLabel}&state=all&sort=created&direction=desc"
                      + $"&since={since}&per_page={PageSize}&page={page}";

            using var request = await NewRequestAsync(HttpMethod.Get, url, cancellationToken);
            using var response = await SendAsync(request, cancellationToken);
            var issues = await ReadAsync<List<IssueResponse>>(response, cancellationToken);

            var match = issues.FirstOrDefault(i =>
                i.PullRequest is null && i.Body != null && i.Body.Contains(marker, StringComparison.Ordinal));
            if (match != null)
                return match.Number;

            if (issues.Count < PageSize)
                break;
        }

        return null;
    }

    private async Task<HttpRequestMessage> NewRequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(cancellationToken));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;

            var error = GitHubErrors.FromStatus(response.StatusCode, response.Headers, _time.GetUtcNow());
            response.Dispose();
            throw error;
        }
        catch (HttpRequestException ex)
        {
            throw GitHubErrors.Network(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw GitHubErrors.Timeout(ex);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                   ?? throw new IssueTrackerException("GitHub returned an empty response", isTransient: true);
        }
        catch (JsonException ex)
        {
            throw new IssueTrackerException("GitHub returned an unreadable response", isTransient: true, inner: ex);
        }
    }

    private sealed record IssueResponse(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("pull_request")] JsonElement? PullRequest);
}
