using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Credfeto.Workflow.GitHub;

public static class GitHubSetup
{
    public const string HttpClientName = "GitHub";

    private const string USER_AGENT = "credfeto-workflow-mcp";
    private const string MEDIA_TYPE = "application/vnd.github+json";
    private const string API_VERSION_HEADER_NAME = "X-GitHub-Api-Version";
    private const string API_VERSION = "2026-03-10";

    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        _ = services.AddOptions<GitHubOptions>().BindConfiguration(GitHubOptions.SectionName).ValidateOnStart();

        return services
            .AddHttpContextAccessor()
            .AddSingleton<IValidateOptions<GitHubOptions>, GitHubOptionsValidator>()
            .AddSingleton<IRepositoryProjectCache, RepositoryProjectCache>()
            .AddSingleton<IGitHubRestClient, GitHubRestClient>()
            .AddSingleton<IGitHubGraphQlClient, GitHubGraphQlClient>()
            .AddTransient<CallerTokenForwardingHandler>()
            .AddHttpClient(name: HttpClientName, configureClient: ConfigureHttpClient)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .RedactLoggedHeaders(static _ => true)
            .AddHttpMessageHandler<CallerTokenForwardingHandler>()
            .AddStandardResilienceHandler(ConfigureResilience)
            .Services;
    }

    // GraphQL is always POST and a mutation must reach GitHub at most once, so unsafe methods are never retried.
    // Every request carries its own caller's token, so one caller's rate limit or 5xx burst must not open a
    // breaker shared by every caller; the circuit breaker is therefore told to count no outcome as a failure.
    // The concurrency limiter is likewise one bucket for every caller, so one caller's parallel paging could
    // starve the rest; GitHub already enforces a rate limit per token, so the limiter is opened up rather than
    // partitioned per caller.
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.ShouldHandle = static _ => PredicateResult.False();
        options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = int.MaxValue;
    }

    // A redirect followed by the primary handler would run below CallerTokenForwardingHandler and could reach any
    // host the Location names without the configured host and base path check, so a 3xx is surfaced as a failure
    private static HttpMessageHandler CreatePrimaryHandler()
    {
        return new SocketsHttpHandler { AllowAutoRedirect = false };
    }

    private static void ConfigureHttpClient(IServiceProvider serviceProvider, HttpClient client)
    {
        GitHubOptions options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;

        client.BaseAddress = options.ApiBaseUrl;
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(productName: USER_AGENT, productVersion: null)
        );
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MEDIA_TYPE));
        client.DefaultRequestHeaders.Add(name: API_VERSION_HEADER_NAME, value: API_VERSION);
    }
}
