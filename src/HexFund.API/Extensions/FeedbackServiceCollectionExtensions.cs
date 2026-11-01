using HexFund.Application.Dispatching;
using HexFund.Application.Interfaces;
using HexFund.Infrastructure.IssueTracking;
using Microsoft.Extensions.Options;

namespace HexFund.API.Extensions;

public static class FeedbackServiceCollectionExtensions
{
    private const int MinRetentionMonths = 1;
    private const int MaxRetentionMonths = 60;

    /// <summary>
    /// Registers the feedback outbox: the dispatcher, retention sweep, and either the real GitHub
    /// tracker or, when <c>GitHub:Enabled</c> is false, a no-op stand-in. Invalid settings fail startup.
    /// </summary>
    public static IServiceCollection AddFeedbackDelivery(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<FeedbackOptions>()
            .Bind(configuration.GetSection(FeedbackOptions.Section))
            .Validate(
                o => o.RetentionMonths is >= MinRetentionMonths and <= MaxRetentionMonths,
                $"Feedback:RetentionMonths must be between {MinRetentionMonths} and {MaxRetentionMonths}.")
            .ValidateOnStart();

        services.AddOptions<GitHubOptions>()
            .Bind(configuration.GetSection(GitHubOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GitHubOptions>, GitHubOptionsValidator>();

        if (configuration.GetValue<bool>($"{GitHubOptions.Section}:Enabled"))
        {
            services.AddSingleton(_ => CreateGitHubHttpClient());
            services.AddSingleton(provider => new GitHubAppTokenProvider(
                provider.GetRequiredService<HttpClient>(),
                provider.GetRequiredService<IOptions<GitHubOptions>>(),
                provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IIssueTracker>(provider => new GitHubIssueTracker(
                provider.GetRequiredService<HttpClient>(),
                provider.GetRequiredService<GitHubAppTokenProvider>(),
                provider.GetRequiredService<IOptions<GitHubOptions>>(),
                provider.GetRequiredService<TimeProvider>()));
        }
        else
        {
            services.AddSingleton<IIssueTracker, NoOpIssueTracker>();
        }

        services.AddSingleton<FeedbackDispatchSignal>();
        services.AddSingleton<IFeedbackDispatchSignal>(p => p.GetRequiredService<FeedbackDispatchSignal>());
        services.AddScoped<FeedbackDispatchProcessor>();
        services.AddScoped<FeedbackRetentionProcessor>();
        services.AddHostedService<FeedbackDispatcherHost>();

        return services;
    }

    /// <summary>
    /// One long-lived client. The pooled connection lifetime makes it pick up DNS changes without
    /// needing the HTTP client factory.
    /// </summary>
    private static HttpClient CreateGitHubHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("HexFund-API");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
