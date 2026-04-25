using System.Diagnostics;

namespace HexFund.API.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var requestMethod = context.Request.Method;

        _logger.LogDebug("HTTP {Method} {Path}", requestMethod, requestPath);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            if (statusCode >= 400)
            {
                _logger.LogWarning(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    requestMethod, requestPath, statusCode, elapsedMs);
            }
            else
            {
                _logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    requestMethod, requestPath, statusCode, elapsedMs);
            }
        }
    }
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
