using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

namespace HexFund.API.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        switch (exception)
        {
            case ArgumentException argumentException:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Bad Request";
                problemDetails.Detail = argumentException.Message;
                break;

            case InvalidOperationException invalidOperationException:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Invalid Operation";
                problemDetails.Detail = invalidOperationException.Message;
                break;

            case UnauthorizedAccessException unauthorizedAccessException:
                problemDetails.Status = (int)HttpStatusCode.Unauthorized;
                problemDetails.Title = "Unauthorized";
                problemDetails.Detail = unauthorizedAccessException.Message;
                break;

            case KeyNotFoundException keyNotFoundException:
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Not Found";
                problemDetails.Detail = keyNotFoundException.Message;
                break;

            default:
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Internal Server Error";
                problemDetails.Detail = "An unexpected error occurred.";
                break;
        }

        context.Response.StatusCode = problemDetails.Status.Value;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, options));
    }
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
