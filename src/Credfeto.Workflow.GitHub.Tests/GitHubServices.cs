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

    public static ServiceProvider Build(string? apiBaseUrl = ApiBaseUrl, HttpMessageHandler? primaryHandler = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(key: "GitHub:ApiBaseUrl", value: apiBaseUrl)])
            .Build();

        IServiceCollection services = new ServiceCollection().AddSingleton(configuration).AddLogging().AddGitHub();

        if (primaryHandler is not null)
        {
            _ = services
                .AddHttpClient(GitHubSetup.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
                .Services.Configure<HttpRetryStrategyOptions>(
                    name: GitHubSetup.HttpClientName,
                    configureOptions: static options => options.Delay = TimeSpan.Zero
                );
        }

        return services.BuildServiceProvider();
    }
}
