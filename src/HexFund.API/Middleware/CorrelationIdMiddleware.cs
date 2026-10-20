using System.Text.RegularExpressions;
using Serilog.Context;

namespace HexFund.API.Middleware;

/// <summary>
/// Gives every request a correlation id so a client report, a response and the server log lines
/// can be matched up. Also records the client's app version for the log context.
/// </summary>
/// <remarks>
/// Must be registered before the exception handler and request logging so errors and the
/// completion log line carry the id. The response header is set in <c>OnStarting</c> because the
/// exception handler clears response headers while it handles an error.
/// </remarks>
public partial class CorrelationIdMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string AppVersionHeader = "X-App-Version";

    /// <summary>Key under which the id is kept in <see cref="HttpContext.Items"/>.</summary>
    public const string ItemKey = "CorrelationId";

    private const int MaxAppVersionLength = 20;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[CorrelationHeader].FirstOrDefault());
        var appVersion = SanitizeAppVersion(context.Request.Headers[AppVersionHeader].FirstOrDefault());

        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("AppVersion", appVersion ?? "-"))
        {
            await _next(context);
        }
    }

    /// <summary>Returns the inbound id when it is well formed, otherwise a new GUID.</summary>
    public static string ResolveCorrelationId(string? inbound) =>
        inbound != null && ValidCorrelationId().IsMatch(inbound)
            ? inbound
            : Guid.NewGuid().ToString();

    /// <summary>
    /// Keeps only letters, digits and <c>. _ + -</c> and cuts to 20 characters, so a client cannot
    /// inject extra log content through the header. Returns null when nothing usable remains.
    /// </summary>
    public static string? SanitizeAppVersion(string? inbound)
    {
        if (string.IsNullOrEmpty(inbound))
            return null;

        var cleaned = InvalidVersionChars().Replace(inbound, string.Empty);
        if (cleaned.Length > MaxAppVersionLength)
            cleaned = cleaned[..MaxAppVersionLength];

        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex ValidCorrelationId();

    [GeneratedRegex(@"[^A-Za-z0-9._+\-]")]
    private static partial Regex InvalidVersionChars();
}
