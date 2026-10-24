using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Credfeto.Workflow.GitHub;

public static class GitHubSetup
{
    public const string HttpClientName = "GitHub";

    private const string USER_AGENT = "credfeto-workflow-mcp";
    private const string MEDIA_TYPE = "application/vnd.github+json";
    private const string API_VERSION_HEADER_NAME = "X-GitHub-Api-Version";
    private const string API_VERSION = "2026-03-10";

    private const string RESILIENCE_PIPELINE_NAME = "resilience";

    // Also the HttpClient timeout, which unlike the resilience pipeline covers buffering the response body
    private static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    // Well inside the total request timeout, so every retry wait leaves room for the attempt that follows it
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        _ = services.AddOptions<GitHubOptions>().BindConfiguration(GitHubOptions.SectionName).ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        // Named after the client so the retry options can be reconfigured for this client alone
        _ = services.AddOptions<HttpRetryStrategyOptions>(HttpClientName).Configure<TimeProvider>(ConfigureRetry);

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
            .AddResilienceHandler(pipelineName: RESILIENCE_PIPELINE_NAME, configure: ConfigureResilience)
            .Services;
    }

    // Every request carries its own caller's token, so there is deliberately no circuit breaker or concurrency
    // limiter: either would be one bucket shared by every caller, letting one caller's failures or parallel paging
    // block the rest, while GitHub already enforces its rate limit per token
    private static void ConfigureResilience(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ResilienceHandlerContext context
    )
    {
        _ = builder
            .AddTimeout(TotalRequestTimeout)
            .AddRetry(context.GetOptions<HttpRetryStrategyOptions>(HttpClientName))
            .AddTimeout(AttemptTimeout);
    }

    // A Retry-After longer than the retry delay cap is not waited out: the wait would run into the total request
    // timeout and replace GitHub's 429 or 503 with a status-less timeout, so that response is returned as-is.
    // GraphQL is always POST and a mutation must reach GitHub at most once, so unsafe methods are never retried;
    // that check is applied last because it wraps whichever predicate is in place when it is called.
    private static void ConfigureRetry(HttpRetryStrategyOptions options, TimeProvider timeProvider)
    {
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> isTransient = options.ShouldHandle;

        options.MaxDelay = MaximumRetryDelay;
        options.ShouldHandle = args =>
            args.Outcome.Result is { } response
            && !RetryAfterFitsDelayCap(response: response, timeProvider: timeProvider)
                ? PredicateResult.False()
                : isTransient(args);
        options.DisableForUnsafeHttpMethods();
    }

    private static bool RetryAfterFitsDelayCap(HttpResponseMessage response, TimeProvider timeProvider)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return true;
        }

        // A Retry-After header always carries either a delta or a date
        TimeSpan delay = retryAfter.Delta ?? retryAfter.Date.GetValueOrDefault() - timeProvider.GetUtcNow();

        return delay <= MaximumRetryDelay;
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
        client.Timeout = TotalRequestTimeout;
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(productName: USER_AGENT, productVersion: null)
        );
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MEDIA_TYPE));
        client.DefaultRequestHeaders.Add(name: API_VERSION_HEADER_NAME, value: API_VERSION);
    }
}
