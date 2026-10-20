using System.IO.Compression;
using System.Security.Claims;
using System.Threading.RateLimiting;
using HexFund.API.Middleware;
using HexFund.Application.Interfaces;
using HexFund.Application.Services;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Caching;
using HexFund.Infrastructure.Data;
using HexFund.Infrastructure.Repositories;
using HexFund.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

namespace HexFund.API.Extensions;

public static class ServiceCollectionExtensions
{

    public static IServiceCollection AddDatabase(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<HexFundDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"), npgsql =>
            {
                npgsql.MaxBatchSize(100);
                npgsql.CommandTimeout(30);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            });

            // Query parameters and detailed errors can contain user data, so they are Development only.
            var isDevelopment = serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment();
            options.EnableSensitiveDataLogging(isDevelopment);
            options.EnableDetailedErrors(isDevelopment);

            // Reads are untracked by default; writers opt in with AsTracking() or Update().
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IFeedbackRepository, FeedbackRepository>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ICashFlowService, CashFlowService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddSingleton<IEntraUserService, EntraUserService>();

        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1024;
            options.CompactionPercentage = 0.25;
        });
        services.AddSingleton<ICacheService, CacheService>();

        return services;
    }

    public static IServiceCollection AddResponseOptimization(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

        return services;
    }

    /// <summary>
    /// Every endpoint requires an authenticated caller unless it opts out with [AllowAnonymous],
    /// so a new controller cannot be public by accident. Set AzureAd:RequiredScope to also
    /// require that delegated scope (the "scp" claim) on every call.
    /// </summary>
    public static IServiceCollection AddApiAuthorization(
        this IServiceCollection services, IConfiguration configuration)
    {
        var requiredScope = configuration["AzureAd:RequiredScope"];

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    string.IsNullOrEmpty(requiredScope)
                    || context.User.FindAll("scp")
                        .Any(c => c.Value.Split(' ').Contains(requiredScope, StringComparer.Ordinal)))
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partition by signed-in user, falling back to the caller address.
            static string PartitionKey(HttpContext context) =>
                context.User.FindFirstValue("oid")
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1)
                }));

            options.AddPolicy(ApiPolicies.AccountDeletionRateLimit, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromMinutes(10)
                }));
        });

        return services;
    }

    public static IServiceCollection AddExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
            });
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "HexFund API",
                Version = "v1",
                Description = "API for managing personal finances with recurring transactions",
                Contact = new OpenApiContact
                {
                    Name = "HexStackIO",
                    Url = new Uri("https://github.com/HexStackIO")
                }
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT access token from Entra External ID.",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(ApiPolicies.Cors, policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                    return;
                }

                // Bearer tokens travel in the Authorization header, so credentialed CORS is not needed.
                var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
                policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
            });
        });

        return services;
    }
}
