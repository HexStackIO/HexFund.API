using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace HexFund.Infrastructure.IssueTracking;

/// <summary>Settings under the <c>GitHub</c> section. Used only to file feedback issues.</summary>
public sealed class GitHubOptions
{
    public const string Section = "GitHub";

    /// <summary>When false the app uses a no-op tracker and needs no GitHub credentials.</summary>
    public bool Enabled { get; set; }

    /// <summary>The GitHub App id (or client id), used as the JWT issuer.</summary>
    public string? AppId { get; set; }

    public string? InstallationId { get; set; }

    /// <summary>The app private key as a PEM file, base64-encoded so it fits in an environment variable.</summary>
    public string? PrivateKeyBase64 { get; set; }

    /// <summary>The private repository that receives issues, as <c>owner/name</c>.</summary>
    public string? TrackerRepo { get; set; }
}

/// <summary>Fails startup when GitHub is enabled but its settings are incomplete or malformed.</summary>
public sealed partial class GitHubOptionsValidator : IValidateOptions<GitHubOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AppId))
            errors.Add("GitHub:AppId is required when GitHub:Enabled is true.");

        if (string.IsNullOrWhiteSpace(options.InstallationId))
            errors.Add("GitHub:InstallationId is required when GitHub:Enabled is true.");

        if (string.IsNullOrWhiteSpace(options.TrackerRepo) || !RepoName().IsMatch(options.TrackerRepo))
            errors.Add("GitHub:TrackerRepo must be in the form owner/name.");

        if (!IsUsablePrivateKey(options.PrivateKeyBase64))
            errors.Add("GitHub:PrivateKeyBase64 must be a base64-encoded PEM RSA private key.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>Decodes a base64 PEM into an RSA key. Never echoes the value.</summary>
    public static RSA? TryLoadKey(string? privateKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(privateKeyBase64))
            return null;

        try
        {
            var pem = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(privateKeyBase64));
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsUsablePrivateKey(string? privateKeyBase64)
    {
        using var rsa = TryLoadKey(privateKeyBase64);
        return rsa != null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+$")]
    private static partial Regex RepoName();
}
