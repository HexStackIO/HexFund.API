using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexFund.API.Extensions;
using HexFund.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HexFund.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    private const string Header = CorrelationIdMiddleware.CorrelationHeader;

    [Theory]
    [InlineData("abcd1234")]
    [InlineData("b1c7e2f0-1111-4222-8333-444455556666")]
    public void ResolveCorrelationId_KeepsWellFormedValues(string inbound) =>
        Assert.Equal(inbound, CorrelationIdMiddleware.ResolveCorrelationId(inbound));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it")]
    [InlineData("bad_chars_here!")]
    [InlineData("line\nbreak-1234")]
    public void ResolveCorrelationId_ReplacesMissingOrMalformedValues(string? inbound)
    {
        var resolved = CorrelationIdMiddleware.ResolveCorrelationId(inbound);

        Assert.NotEqual(inbound, resolved);
        Assert.True(Guid.TryParse(resolved, out _));
    }

    [Fact]
    public void ResolveCorrelationId_RejectsValuesLongerThan64Characters() =>
        Assert.True(Guid.TryParse(CorrelationIdMiddleware.ResolveCorrelationId(new string('a', 65)), out _));

    [Theory]
    [InlineData("1.9", "1.9")]
    [InlineData("1.9.0+build-23", "1.9.0+build-23")]
    [InlineData("1.9 beta\r\nInjected: x", "1.9betaInjectedx")]
    [InlineData("123456789012345678901234567890", "12345678901234567890")]
    [InlineData("!!!", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SanitizeAppVersion_StripsUnsafeCharactersAndTruncates(string? inbound, string? expected) =>
        Assert.Equal(expected, CorrelationIdMiddleware.SanitizeAppVersion(inbound));

    [Fact]
    public async Task WellFormedInboundId_IsEchoedOnTheResponse()
    {
        using var server = CreateServer();
        var request = new HttpRequestMessage(HttpMethod.Get, "/ok");
        request.Headers.Add(Header, "client-id-12345");

        var response = await server.CreateClient().SendAsync(request);

        Assert.Equal("client-id-12345", response.Headers.GetValues(Header).Single());
    }

    [Fact]
    public async Task MissingOrInvalidInboundId_GetsAGeneratedGuid()
    {
        using var server = CreateServer();

        var missing = await server.CreateClient().GetAsync("/ok");
        Assert.True(Guid.TryParse(missing.Headers.GetValues(Header).Single(), out _));

        var invalid = new HttpRequestMessage(HttpMethod.Get, "/ok");
        invalid.Headers.Add(Header, "x");
        var response = await server.CreateClient().SendAsync(invalid);
        Assert.True(Guid.TryParse(response.Headers.GetValues(Header).Single(), out _));
    }

    [Fact]
    public async Task ErrorResponses_CarryTheHeaderAndAProblemDetailsExtension()
    {
        using var server = CreateServer();
        var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add(Header, "client-id-12345");

        var response = await server.CreateClient().SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("client-id-12345", response.Headers.GetValues(Header).Single());
        Assert.Equal("client-id-12345", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task ValidationStyleErrors_AlsoGetTheExtension()
    {
        using var server = CreateServer();

        var response = await server.CreateClient().GetAsync("/bad");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(response.Headers.GetValues(Header).Single(), problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task OversizedRequests_MapTo413()
    {
        using var server = CreateServer();

        var response = await server.CreateClient().GetAsync("/too-big");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static TestServer CreateServer() => new(new WebHostBuilder()
        .ConfigureLogging(logging => logging.ClearProviders())
        .ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddExceptionHandling();
        })
        .Configure(app =>
        {
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseExceptionHandler();
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/ok", () => "ok");
                endpoints.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("secret detail")));
                endpoints.MapGet("/bad", (Func<string>)(() => throw new ArgumentException("nope")));
                endpoints.MapGet("/too-big", (Func<string>)(() =>
                    throw new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge)));
            });
        }));
}
