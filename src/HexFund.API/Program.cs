using HexFund.API.Extensions;
using HexFund.API.Middleware;
using HexFund.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Serilog;

// Bootstrap logger so startup failures are captured before configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

const string LogOutputTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] [v{AppVersion}] {Message:lj}{NewLine}{Exception}";

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Levels and overrides come from the "Serilog" section of appsettings.
    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        // Defaults for log lines written outside a request; request values from LogContext win.
        .Enrich.WithProperty("CorrelationId", "-")
        .Enrich.WithProperty("AppVersion", "-")
        .WriteTo.Console(outputTemplate: LogOutputTemplate)
        .WriteTo.File(
            path: "logs/hexfund-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: LogOutputTemplate));

    builder.Services.AddResponseOptimization();
    builder.Services.AddDatabase(builder.Configuration);
    builder.Services.AddApplicationServices();
    builder.Services.AddCorsPolicy(builder.Environment, builder.Configuration);
    builder.Services.AddExceptionHandling();
    builder.Services.AddApiAuthorization(builder.Configuration);
    builder.Services.AddApiRateLimiting();
    builder.Services.AddFeedbackDelivery(builder.Configuration);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(
            options => options.Audience = builder.Configuration["AzureAd:ClientId"],
            identityOptions =>
            {
                identityOptions.Authority =
                    $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                identityOptions.ClientId = builder.Configuration["AzureAd:ClientId"];
                identityOptions.TenantId = builder.Configuration["AzureAd:TenantId"];
            });

    // [ApiController] returns a ValidationProblemDetails 400 for invalid models automatically.
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

    builder.Services.AddSwagger();

    var app = builder.Build();

    app.UseResponseCompression();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "HexFund API v1");
            c.RoutePrefix = string.Empty;
        });
    }

    app.UseHttpsRedirection();
    app.UseCors(ApiPolicies.Cors);
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();
    app.MapControllers();

    // Production schema changes are applied by the deployment pipeline (see the migrate job);
    // applying them on startup is a local-development convenience only.
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HexFundDbContext>().Database.MigrateAsync();
    }

    Log.Information("Running | Environment: {Environment}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
