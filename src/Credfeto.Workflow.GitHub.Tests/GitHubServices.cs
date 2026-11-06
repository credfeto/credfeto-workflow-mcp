using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Credfeto.Workflow.GitHub.Tests;

internal static class GitHubServices
{
    public const string ApiBaseUrl = "https://github-proxy.example/";

    public static ServiceProvider Build(
        string? apiBaseUrl = ApiBaseUrl,
        HttpMessageHandler? primaryHandler = null,
        TimeSpan? httpClientTimeout = null,
        long? maxResponseContentBufferSize = null,
        TimeSpan? attemptTimeout = null,
        string? apiVersion = null
    )
    {
        List<KeyValuePair<string, string?>> settings = [new(key: "GitHub:ApiBaseUrl", value: apiBaseUrl)];

        if (apiVersion is not null)
        {
            settings.Add(new(key: "GitHub:ApiVersion", value: apiVersion));
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        IServiceCollection services = new ServiceCollection().AddSingleton(configuration).AddLogging().AddGitHub();

        if (primaryHandler is not null)
        {
            _ = services
                .AddHttpClient(GitHubSetup.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
                .ConfigureHttpClient(client =>
                {
                    client.Timeout = httpClientTimeout ?? client.Timeout;
                    client.MaxResponseContentBufferSize =
                        maxResponseContentBufferSize ?? client.MaxResponseContentBufferSize;
                })
                .Services.Configure<HttpRetryStrategyOptions>(
                    name: GitHubSetup.HttpClientName,
                    configureOptions: static options => options.Delay = TimeSpan.Zero
                )
                .Configure<HttpTimeoutStrategyOptions>(
                    name: GitHubSetup.HttpClientName,
                    configureOptions: options => options.Timeout = attemptTimeout ?? options.Timeout
                );
        }

        return services.BuildServiceProvider();
    }
}
