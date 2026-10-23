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
            .RedactLoggedHeaders(static _ => true)
            .AddHttpMessageHandler<CallerTokenForwardingHandler>()
            .AddStandardResilienceHandler(ConfigureResilience)
            .Services;
    }

    // GraphQL is always POST and a mutation must reach GitHub at most once, so unsafe methods are never retried
    private static void ConfigureResilience(HttpStandardResilienceOptions options)
    {
        options.Retry.DisableForUnsafeHttpMethods();
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
