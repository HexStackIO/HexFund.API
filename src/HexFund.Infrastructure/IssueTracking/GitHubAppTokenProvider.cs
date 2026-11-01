using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexFund.Application.Dispatching;
using Microsoft.Extensions.Options;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>
/// Authenticates as a GitHub App: signs a short-lived JWT with the app private key, exchanges it for
/// an installation access token, and caches that token until shortly before it expires.
/// </summary>
/// <remarks>Neither the key, the JWT nor the token is ever logged.</remarks>
public sealed class GitHubAppTokenProvider : IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly GitHubOptions _options;
    private readonly TimeProvider _time;
    private readonly RSA _key;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public GitHubAppTokenProvider(HttpClient http, IOptions<GitHubOptions> options, TimeProvider time)
    {
        _http = http;
        _options = options.Value;
        _time = time;
        _key = GitHubOptionsValidator.TryLoadKey(_options.PrivateKeyBase64)
            ?? throw new InvalidOperationException("GitHub:PrivateKeyBase64 is not a usable RSA key.");
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh())
            return _token!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh())
                return _token!;

            var response = await RequestInstallationTokenAsync(cancellationToken);
            _token = response.Token;
            _expiresAt = response.ExpiresAt;
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh() => _token != null && _time.GetUtcNow() < _expiresAt - RefreshMargin;

    private async Task<InstallationToken> RequestInstallationTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"app/installations/{_options.InstallationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAppJwt());

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw GitHubErrors.FromStatus(response.StatusCode, response.Headers, _time.GetUtcNow());

            var body = await response.Content.ReadFromJsonAsync<InstallationToken>(cancellationToken);
            if (body is null || string.IsNullOrEmpty(body.Token))
                throw new IssueTrackerException("GitHub returned an empty installation token", isTransient: true);

            return body;
        }
        catch (HttpRequestException ex)
        {
            throw GitHubErrors.Network(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw GitHubErrors.Timeout(ex);
        }
        catch (JsonException ex)
        {
            throw new IssueTrackerException("GitHub returned an unreadable token response", isTransient: true, inner: ex);
        }
    }

    /// <summary>An RS256 JWT valid for under ten minutes, as GitHub requires for app authentication.</summary>
    internal string CreateAppJwt()
    {
        var now = _time.GetUtcNow();
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = _options.AppId
        }));

        var signingInput = $"{header}.{payload}";
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _key.Dispose();
        _gate.Dispose();
    }

    private sealed record InstallationToken(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
