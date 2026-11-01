using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using HexFund.API.Controllers;
using HexFund.API.Extensions;
using HexFund.API.Middleware;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.Core.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HexFund.Tests.Middleware;

public class FeedbackEndpointTests
{
    private sealed class StubFeedbackService : IFeedbackService
    {
        public Guid? ReceivedUser { get; private set; }
        public SubmitFeedbackRequest? ReceivedRequest { get; private set; }
        public FeedbackReceiptDto? Result { get; set; } = new() { Id = Guid.NewGuid() };

        public Task<FeedbackReceiptDto?> SubmitAsync(Guid userId, SubmitFeedbackRequest request)
        {
            ReceivedUser = userId;
            ReceivedRequest = request;
            return Task.FromResult(Result);
        }
    }

    private static ControllerContext ContextFor(Guid userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", userId.ToString()) }, "test"))
        }
    };

    private static SubmitFeedbackRequest Report() =>
        new() { Type = FeedbackType.Bug, Body = "The calendar shows rent on the wrong day." };

    [Fact]
    public async Task Submit_ReturnsAcceptedWithTheReceipt_ForTheCallingUser()
    {
        var userId = Guid.NewGuid();
        var service = new StubFeedbackService();
        var controller = new FeedbackController(service) { ControllerContext = ContextFor(userId) };

        var result = await controller.Submit(Report());

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(service.Result!.Id, ((FeedbackReceiptDto)accepted.Value!).Id);
        Assert.Equal(userId, service.ReceivedUser);
    }

    [Fact]
    public async Task Submit_ForAnUnsyncedUser_IsA404ProblemThatPointsAtSync()
    {
        var service = new StubFeedbackService { Result = null };
        var controller = new FeedbackController(service) { ControllerContext = ContextFor(Guid.NewGuid()) };

        var result = await controller.Submit(Report());

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Contains("auth/sync", ((ProblemDetails)problem.Value!).Detail);
    }

    [Fact]
    public async Task FeedbackPolicy_AllowsFiveReportsPerWindow_ThenReturns429WithRetryAfter()
    {
        using var server = CreateRateLimitedServer();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.CorrelationHeader, "client-id-12345");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/feedback", null)).StatusCode);

        var rejected = await client.PostAsync("/feedback", null);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = int.Parse(rejected.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 3600);
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("client-id-12345", problem.GetProperty("correlationId").GetString());
        Assert.Contains(retryAfter.ToString(), problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task FeedbackPolicy_CountsEachUserSeparately()
    {
        using var server = CreateRateLimitedServer();

        HttpClient ClientFor(string oid)
        {
            var client = server.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
            return client;
        }

        var first = ClientFor("11111111-1111-1111-1111-111111111111");
        for (var i = 0; i < 5; i++)
            await first.PostAsync("/feedback", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsync("/feedback", null)).StatusCode);

        var second = ClientFor("22222222-2222-2222-2222-222222222222");
        Assert.Equal(HttpStatusCode.Accepted, (await second.PostAsync("/feedback", null)).StatusCode);
    }

    private static TestServer CreateRateLimitedServer() => new(new WebHostBuilder()
        .ConfigureLogging(logging => logging.ClearProviders())
        .ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddExceptionHandling();
            services.AddApiRateLimiting();
        })
        .Configure(app =>
        {
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseExceptionHandler();
            app.UseRouting();

            // Stands in for authentication: the test header becomes the oid claim the limiter reads.
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Oid", out var oid))
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", oid.ToString()) }, "test"));
                await next();
            });

            app.UseRateLimiter();
            app.UseEndpoints(endpoints =>
                endpoints.MapPost("/feedback", () => Results.Accepted())
                    .RequireRateLimiting(ApiPolicies.FeedbackRateLimit));
        }));
}
