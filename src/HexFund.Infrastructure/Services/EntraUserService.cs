using Azure.Identity;
using HexFund.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace HexFund.Infrastructure.Services;

/// <summary>
/// Deletes users from Entra External ID (CIAM) via the Microsoft Graph API
/// using client credentials (app-only) authentication.
///
/// Root cause of the production 404:
///   The default GraphServiceClient credential resolves tokens against
///   login.microsoftonline.com (global AAD). CIAM tenants use a
///   tenant-specific authority (e.g. financeplannerapp.ciamlogin.com).
///   A token issued by the wrong authority is accepted by Graph but
///   resolves against the wrong directory, so every user ID returns 404.
///   Pointing TokenCredentialOptions.AuthorityHost at the CIAM instance
///   ensures the token is issued by and validated against the correct tenant.
///
/// Required app registration permissions (application, not delegated):
///   Microsoft Graph → User.ReadWrite.All
///   Admin consent must be granted in the CIAM tenant.
///
/// Required config / environment variables:
///   AzureAd:TenantId      — CIAM tenant ID (GUID)
///   AzureAd:ClientId      — app registration client ID
///   AzureAd:ClientSecret  — client secret (env var only, never in source)
///   AzureAd:Instance      — CIAM base, e.g. "https://financeplannerapp.ciamlogin.com/"
/// </summary>
public class EntraUserService : IEntraUserService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<EntraUserService> _logger;

    public EntraUserService(IConfiguration configuration, ILogger<EntraUserService> logger)
    {
        _logger = logger;

        var tenantId     = configuration["AzureAd:TenantId"]
                           ?? throw new InvalidOperationException("AzureAd:TenantId is not configured.");
        var clientId     = configuration["AzureAd:ClientId"]
                           ?? throw new InvalidOperationException("AzureAd:ClientId is not configured.");
        var clientSecret = configuration["AzureAd:ClientSecret"]
                           ?? throw new InvalidOperationException(
                               "AzureAd:ClientSecret is not configured. " +
                               "Set this via environment variable in production.");
        var instance     = configuration["AzureAd:Instance"]
                           ?? throw new InvalidOperationException("AzureAd:Instance is not configured.");

        // Build the CIAM-specific authority URI from the Instance + TenantId.
        // e.g. https://financeplannerapp.ciamlogin.com/{tenantId}
        // Without this, ClientSecretCredential defaults to login.microsoftonline.com
        // which issues a token for the global AAD directory — causing all CIAM
        // user lookups to return 404.
        var authorityHost = new Uri($"{instance.TrimEnd('/')}/{tenantId}");

        var credential = new ClientSecretCredential(
            tenantId,
            clientId,
            clientSecret,
            new TokenCredentialOptions { AuthorityHost = authorityHost });

        // The Graph base URL (https://graph.microsoft.com/v1.0) is correct for
        // CIAM — the fix is entirely in the token authority above.
        _graphClient = new GraphServiceClient(
            credential,
            new[] { "https://graph.microsoft.com/.default" });
    }

    public async Task<bool> DeleteUserAsync(string entraObjectId)
    {
        try
        {
            await _graphClient.Users[entraObjectId].DeleteAsync();

            _logger.LogWarning(
                "Entra user {EntraObjectId} permanently deleted via Graph API.",
                entraObjectId);

            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Not found — already deleted or never fully synced. Non-fatal.
            _logger.LogWarning(
                "Entra user {EntraObjectId} not found in Entra directory (already removed?).",
                entraObjectId);
            return false;
        }
        // 403 (missing permissions / consent), 503 (Graph outage), etc.
        // propagate so AuthService can abort and surface a clear error.
    }
}
