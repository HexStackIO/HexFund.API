using Azure.Identity;
using HexFund.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace HexFund.Infrastructure.Services;

/// <summary>
/// Deletes users from Entra External ID via the Microsoft Graph API using
/// client credentials (app-only) authentication.
///
/// Required app registration permissions (application, not delegated):
///   Microsoft Graph → User.ReadWrite.All  (or Directory.ReadWrite.All)
///   Admin consent must be granted.
///
/// Required appsettings / environment variables:
///   AzureAd:TenantId      — your Entra tenant ID
///   AzureAd:ClientId      — your app registration client ID
///   AzureAd:ClientSecret  — a client secret for the app registration
///                           (set via environment variable in production,
///                            never commit to source control)
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
                               "Set this via environment variable in production, never in appsettings.json.");

        // ClientSecretCredential uses the OAuth 2.0 client credentials grant —
        // app-only auth, no user context needed.
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _graphClient = new GraphServiceClient(credential,
            new[] { "https://graph.microsoft.com/.default" });
    }

    public async Task<bool> DeleteUserAsync(string entraObjectId)
    {
        try
        {
            // Graph DELETE /users/{id} — in Entra External ID (CIAM) tenants
            // this permanently removes the user. In standard AAD tenants it
            // moves them to the soft-delete bin (30-day recovery window).
            // Verify the behaviour in your Azure portal for your tenant type.
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
                "Entra user {EntraObjectId} not found in directory (already removed?).",
                entraObjectId);
            return false;
        }
        // Any other error (403 permission denied, 503, etc.) propagates so
        // AuthService can surface a meaningful error to the client.
    }
}
