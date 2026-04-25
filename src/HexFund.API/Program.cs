using HexFund.API.Extensions;
using HexFund.API.Middleware;
using HexFund.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Serilog;
using Serilog.Events;

// Configure Serilog early so startup errors are captured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/hexfund-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting up");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Services
    builder.Services.AddResponseOptimization();
    builder.Services.AddDatabase(builder.Configuration);
    builder.Services.AddApplicationServices();
    builder.Services.AddCorsPolicy(builder.Environment, builder.Configuration);

    try
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(options =>
            {
                options.Audience = builder.Configuration["AzureAd:ClientId"];
            },
            identityOptions =>
            {
                identityOptions.Authority = $"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                identityOptions.ClientId = builder.Configuration["AzureAd:ClientId"];
                identityOptions.TenantId = builder.Configuration["AzureAd:TenantId"];
            });
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Authentication setup failed");
        throw;
    }

    builder.Services.AddControllers(options =>
    {
        // ── Global validation filter ──────────────────────────────────────
        // Handles ModelState rejection AND input sanitization for every
        // action automatically. Individual controllers no longer need
        // if (!ModelState.IsValid) guards.
        options.Filters.Add<ValidationActionFilter>();
    })
        .ConfigureApiBehaviorOptions(options =>
        {
            // Suppress the default automatic 400 response from [ApiController]
            // so our ValidationActionFilter controls the response shape instead,
            // giving us a consistent ValidationErrorResponse format everywhere.
            options.SuppressModelStateInvalidFilter = true;
        })
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwagger();

    // Middleware pipeline
    var app = builder.Build();

    app.UseResponseCompression();
    app.UseGlobalExceptionHandler();
    app.UseRequestLogging();
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
    app.UseCors("AllowAll");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    // Migrate on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<HexFundDbContext>();
        try
        {
            if (db.Database.CanConnect())
            {
                Log.Information("DB connected");
                var pending = db.Database.GetPendingMigrations().ToList();
                if (pending.Count > 0)
                {
                    Log.Information("Applying {Count} pending migration(s)", pending.Count);
                    db.Database.Migrate();
                    Log.Information("Migrations applied");
                }
                else
                {
                    Log.Information("DB up to date");
                }
            }
            else
            {
                Log.Warning("Cannot connect to database");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DB startup error: {Message}", ex.Message);
        }
    }

    Log.Information("Running | Environment: {Environment}", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}