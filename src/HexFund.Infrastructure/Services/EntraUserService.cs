using Azure.Identity;
using HexFund.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace HexFund.Infrastructure.Services;

/// <summary>
/// Deletes users from Entra External ID through Microsoft Graph, authenticating as the app
/// (client credentials).
/// </summary>
/// <remarks>
/// Needs the Microsoft Graph application permission <c>User.ReadWrite.All</c> with admin consent
/// in the External ID tenant, and these settings: <c>AzureAd:Instance</c>, <c>AzureAd:TenantId</c>,
/// <c>AzureAd:ClientId</c> and <c>AzureAd:ClientSecret</c> (the secret comes from an environment
/// variable, never from source).
/// </remarks>
public class EntraUserService : IEntraUserService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<EntraUserService> _logger;

    public EntraUserService(IConfiguration configuration, ILogger<EntraUserService> logger)
    {
        _logger = logger;

        var tenantId = configuration["AzureAd:TenantId"]
            ?? throw new InvalidOperationException("AzureAd:TenantId is not configured.");
        var clientId = configuration["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("AzureAd:ClientId is not configured.");
        var clientSecret = configuration["AzureAd:ClientSecret"]
            ?? throw new InvalidOperationException("AzureAd:ClientSecret is not configured.");
        var instance = configuration["AzureAd:Instance"]
            ?? throw new InvalidOperationException("AzureAd:Instance is not configured.");

        // External ID tenants have their own authority (for example <name>.ciamlogin.com).
        // With the default authority the token comes from the global directory, and Graph then
        // returns 404 for every External ID user.
        var authorityHost = new Uri($"{instance.TrimEnd('/')}/{tenantId}");

        var credential = new ClientSecretCredential(
            tenantId,
            clientId,
            clientSecret,
            new TokenCredentialOptions { AuthorityHost = authorityHost });

        _graphClient = new GraphServiceClient(
            credential,
            new[] { "https://graph.microsoft.com/.default" });
    }

    public async Task<bool> DeleteUserAsync(string entraObjectId)
    {
        try
        {
            await _graphClient.Users[entraObjectId].DeleteAsync();

            _logger.LogWarning("Entra user {EntraObjectId} permanently deleted.", entraObjectId);
            return true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Already deleted, or never fully created. Not an error for the caller.
            _logger.LogWarning("Entra user {EntraObjectId} was not found in the directory.", entraObjectId);
            return false;
        }
        // Other failures (403 missing consent, 5xx outage) propagate so deletion can be retried.
    }
}
