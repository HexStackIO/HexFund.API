using HexFund.Application.Interfaces;
using HexFund.Application.Services;
using HexFund.Core.Interfaces;
using HexFund.Infrastructure.Caching;
using HexFund.Infrastructure.Data;
using HexFund.Infrastructure.Repositories;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.IO.Compression;

namespace HexFund.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<HexFundDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MaxBatchSize(100);
                npgsqlOptions.CommandTimeout(30);
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            // Only log query details in Development
            var env = services.BuildServiceProvider().GetRequiredService<IWebHostEnvironment>();
            options.EnableSensitiveDataLogging(env.IsDevelopment());
            options.EnableDetailedErrors(env.IsDevelopment());
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ICashFlowService, CashFlowService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<TransactionRecurrenceService>();
        services.AddScoped<ICacheService, CacheService>();

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

        services.Configure<BrotliCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);

        services.Configure<GzipCompressionProviderOptions>(options =>
            options.Level = CompressionLevel.Fastest);

        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1024;
            options.CompactionPercentage = 0.25;
        });

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
                Description = "JWT Authorization header. Enter 'Bearer' [space] and then your token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
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
            options.AddPolicy("AllowAll", policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                }
                else
                {
                    var allowedOrigins = configuration
                        .GetSection("AllowedOrigins")
                        .Get<string[]>() ?? Array.Empty<string>();

                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            });
        });

        return services;
    }
}
