using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HexFund.Application.Dispatching;
using HexFund.Application.Issues;
using HexFund.Infrastructure.IssueTracking;
using Microsoft.Extensions.Options;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Services;

public class GitHubIntegrationTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly FakeClock _clock = new(Utc(2026, 10, 20, 12));
    private readonly ScriptedHandler _handler = new();
    private readonly GitHubOptions _options;
    private readonly GitHubAppTokenProvider _tokens;
    private readonly GitHubIssueTracker _tracker;

    public GitHubIntegrationTests()
    {
        _options = new GitHubOptions
        {
            Enabled = true,
            AppId = "12345",
            InstallationId = "999",
            TrackerRepo = "HexStackIO/HexFund.Feedback",
            PrivateKeyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportRSAPrivateKeyPem()))
        };

        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(_options);
        _tokens = new GitHubAppTokenProvider(http, options, _clock);
        _tracker = new GitHubIssueTracker(http, _tokens, options, _clock);
    }

    public void Dispose()
    {
        _tokens.Dispose();
        _rsa.Dispose();
    }

    private static IssueDraft Draft() => new("[Bug] title", "body", new[] { "needs-triage", "source-app", "bug" });

    private static string TokenJson(DateTimeOffset expires, string token = "ghs_token") =>
        $$"""{"token":"{{token}}","expires_at":"{{expires:yyyy-MM-dd'T'HH:mm:ss'Z'}}"}""";

    private void ExpectToken(string token = "ghs_token") =>
        _handler.Enqueue(HttpStatusCode.Created, TokenJson(_clock.GetUtcNow().AddHours(1), token));

    [Fact]
    public async Task CreateIssue_AuthenticatesAsTheInstallation_AndPostsTitleBodyAndLabels()
    {
        ExpectToken();
        _handler.Enqueue(HttpStatusCode.Created, """{"number":42}""");

        var number = await _tracker.CreateIssueAsync(Draft(), CancellationToken.None);

        Assert.Equal(42, number);

        var tokenRequest = _handler.Requests[0];
        Assert.Equal("POST", tokenRequest.Method);
        Assert.Equal("/app/installations/999/access_tokens", tokenRequest.Path);
        Assert.StartsWith("Bearer ", tokenRequest.Authorization);

        var issueRequest = _handler.Requests[1];
        Assert.Equal("/repos/HexStackIO/HexFund.Feedback/issues", issueRequest.Path);
        Assert.Equal("Bearer ghs_token", issueRequest.Authorization);

        using var body = JsonDocument.Parse(issueRequest.Body!);
        Assert.Equal("[Bug] title", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("labels").GetArrayLength());
    }

    [Fact]
    public async Task AppJwt_IsRs256WithAShortLifetime_AndVerifiesAgainstThePublicKey()
    {
        ExpectToken();
        _handler.Enqueue(HttpStatusCode.Created, """{"number":1}""");
        await _tracker.CreateIssueAsync(Draft(), CancellationToken.None);

        var jwt = _handler.Requests[0].Authorization!["Bearer ".Length..];
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(FromBase64Url(parts[0]));
        using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("12345", payload.RootElement.GetProperty("iss").GetString());

        var issuedAt = payload.RootElement.GetProperty("iat").GetInt64();
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        var expires = payload.RootElement.GetProperty("exp").GetInt64();
        Assert.True(expires - now < 600, "GitHub rejects app JWTs that expire more than 10 minutes from now.");
        Assert.True(issuedAt < now, "iat is backdated to tolerate clock drift.");

        Assert.True(_rsa.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            FromBase64UrlBytes(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task InstallationToken_IsCachedUntilFiveMinutesBeforeExpiry()
    {
        ExpectToken("first");
        _handler.Enqueue(HttpStatusCode.Created, """{"number":1}""");
        _handler.Enqueue(HttpStatusCode.Created, """{"number":2}""");
        await _tracker.CreateIssueAsync(Draft(), CancellationToken.None);
        await _tracker.CreateIssueAsync(Draft(), CancellationToken.None);

        Assert.Equal(1, _handler.Requests.Count(r => r.Path.EndsWith("access_tokens")));

        _clock.Advance(TimeSpan.FromMinutes(56));
        ExpectToken("second");
        _handler.Enqueue(HttpStatusCode.Created, """{"number":3}""");
        await _tracker.CreateIssueAsync(Draft(), CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count(r => r.Path.EndsWith("access_tokens")));
        Assert.Equal("Bearer second", _handler.Requests[^1].Authorization);
    }

    [Fact]
    public async Task FindIssue_MatchesTheMarkerInABody_IgnoringPullRequests()
    {
        ExpectToken();
        _handler.Enqueue(HttpStatusCode.OK, """
            [
              {"number":10,"body":"unrelated"},
              {"number":11,"body":"<!-- hexfund-feedback:abc -->","pull_request":{}},
              {"number":12,"body":"x\n<!-- hexfund-feedback:abc -->\ny"}
            ]
            """);

        var found = await _tracker.FindIssueByMarkerAsync("hexfund-feedback:abc", Utc(2026, 10, 20), CancellationToken.None);

        Assert.Equal(12, found);
        var query = _handler.Requests[1].Path + _handler.Requests[1].Query;
        Assert.Contains("labels=source-app", query);
        Assert.Contains("state=all", query);
        Assert.Contains("since=2026-10-20T00%3A00%3A00Z", query);
    }

    [Fact]
    public async Task FindIssue_ReturnsNull_WhenNoBodyContainsTheMarker()
    {
        ExpectToken();
        _handler.Enqueue(HttpStatusCode.OK, """[{"number":10,"body":"unrelated"}]""");

        Assert.Null(await _tracker.FindIssueByMarkerAsync("hexfund-feedback:zzz", Utc(2026, 10, 20), CancellationToken.None));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(429, true)]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(404, true)]
    [InlineData(422, false)]
    [InlineData(400, false)]
    public async Task HttpErrors_AreClassifiedAsTransientOrPermanent(int status, bool transient)
    {
        ExpectToken();
        _handler.Enqueue((HttpStatusCode)status, "{\"message\":\"details that must not be kept\"}");

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.Equal(transient, ex.IsTransient);
        Assert.Equal($"GitHub returned {status}", ex.Message);
    }

    [Fact]
    public async Task RateLimitResponse_CarriesTheRetryAfterDelay()
    {
        ExpectToken();
        _handler.Enqueue(HttpStatusCode.Forbidden, "{}", ("Retry-After", "120"));

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.True(ex.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(120), ex.RetryAfter);
    }

    [Fact]
    public async Task ExhaustedRateLimit_UsesTheResetTime()
    {
        ExpectToken();
        var reset = _clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds().ToString();
        _handler.Enqueue(HttpStatusCode.Forbidden, "{}", ("x-ratelimit-remaining", "0"), ("x-ratelimit-reset", reset));

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.Equal(TimeSpan.FromMinutes(10), ex.RetryAfter);
    }

    [Fact]
    public async Task NetworkFailure_IsTransient_AndDoesNotLeakDetails()
    {
        ExpectToken();
        _handler.EnqueueException(new HttpRequestException("connection reset to 10.0.0.1"));

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.True(ex.IsTransient);
        Assert.DoesNotContain("10.0.0.1", ex.Message);
    }

    [Fact]
    public async Task TimeoutWithoutCallerCancellation_IsTransient()
    {
        ExpectToken();
        _handler.EnqueueException(new TaskCanceledException("timed out"));

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task TokenRequestFailure_SurfacesAsATrackerError()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<IssueTrackerException>(() => _tracker.CreateIssueAsync(Draft(), CancellationToken.None));

        Assert.True(ex.IsTransient);
        Assert.Equal("GitHub returned 401", ex.Message);
    }

    [Fact]
    public void Options_WhenDisabled_NeedNoSettings() =>
        Assert.True(new GitHubOptionsValidator().Validate(null, new GitHubOptions { Enabled = false }).Succeeded);

    [Fact]
    public void Options_WhenEnabled_AcceptACompleteConfiguration() =>
        Assert.True(new GitHubOptionsValidator().Validate(null, _options).Succeeded);

    [Fact]
    public void Options_WhenEnabled_ReportEveryProblem_WithoutEchoingTheKey()
    {
        var bad = new GitHubOptions
        {
            Enabled = true,
            AppId = "",
            InstallationId = null,
            TrackerRepo = "not-a-repo",
            PrivateKeyBase64 = "c2VjcmV0LWtleS12YWx1ZQ=="
        };

        var result = new GitHubOptionsValidator().Validate(null, bad);

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures!.Count());
        Assert.DoesNotContain(result.Failures!, f => f.Contains("c2VjcmV0"));
    }

    private static byte[] FromBase64UrlBytes(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    private static string FromBase64Url(string value) => Encoding.UTF8.GetString(FromBase64UrlBytes(value));

    private sealed record RecordedRequest(string Method, string Path, string Query, string? Authorization, string? Body);

    /// <summary>Returns queued responses in order and records what was sent.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<RecordedRequest> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string json, params (string Name, string Value)[] headers) =>
            _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                foreach (var (name, value) in headers)
                    response.Headers.TryAddWithoutValidation(name, value);
                return response;
            });

        public void EnqueueException(Exception exception) => _responses.Enqueue(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Query,
                request.Headers.Authorization?.ToString(),
                body));

            return _responses.Dequeue()();
        }
    }
}
