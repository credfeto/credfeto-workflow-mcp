using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubHttpClientPipelineTests : TestBase
{
    private const int STANDARD_RETRY_ATTEMPTS = 3;

    // Comfortably above the standard circuit breaker's default minimum throughput of 100 requests
    private const int REQUESTS_BEYOND_BREAKER_MINIMUM_THROUGHPUT = 150;

    private static readonly Uri UserUri = new(uriString: "user", uriKind: UriKind.Relative);

    [Fact]
    public async Task ClientIsConfiguredForGitHubAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        using HttpClient client = CreateClient(serviceProvider);

        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl), actual: client.BaseAddress);
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal(expected: "application/vnd.github+json", actual: client.DefaultRequestHeaders.Accept.ToString());
        Assert.True(
            condition: client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"),
            userMessage: "API version header should be set"
        );
    }

    [Fact]
    public async Task LoggedHeaderValuesAreRedactedAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        HttpClientFactoryOptions options = serviceProvider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(GitHubSetup.HttpClientName);

        Assert.True(
            condition: options.ShouldRedactHeaderValue("Authorization"),
            userMessage: "Authorization must be redacted"
        );
    }

    [Theory]
    [InlineData("POST", 1)]
    [InlineData("GET", STANDARD_RETRY_ATTEMPTS + 1)]
    public async Task OnlyGetIsRetriedOnTransientFailureAsync(string method, int expectedRequests)
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.ServiceUnavailable)
        );

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Parse(method), upstream: upstream);

        Assert.Equal(expected: HttpStatusCode.ServiceUnavailable, actual: response.StatusCode);
        Assert.Equal(expected: expectedRequests, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task RepeatedFailuresDoNotOpenASharedCircuitAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.ServiceUnavailable)
        );
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithBearerToken();

        using HttpClient client = CreateClient(serviceProvider);

        for (int attempt = 0; attempt < REQUESTS_BEYOND_BREAKER_MINIMUM_THROUGHPUT; ++attempt)
        {
            using HttpRequestMessage request = new(method: HttpMethod.Post, requestUri: UserUri);
            using HttpResponseMessage response = await client.SendAsync(
                request: request,
                cancellationToken: this.CancellationToken()
            );

            Assert.Equal(expected: HttpStatusCode.ServiceUnavailable, actual: response.StatusCode);
        }

        Assert.Equal(expected: REQUESTS_BEYOND_BREAKER_MINIMUM_THROUGHPUT, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CallerTokenIsForwardedThroughThePipelineAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Get, upstream: upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + CallerContext.Token, actual: request.Authorization);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + "user"), actual: request.RequestUri);
    }

    [Fact]
    public async Task MissingCallerTokenSendsNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithAuthorization(null);

        using HttpClient client = CreateClient(serviceProvider);

        await Assert.ThrowsAsync<CallerTokenMissingException>(() =>
            client.GetAsync(requestUri: UserUri, cancellationToken: this.CancellationToken())
        );

        Assert.Empty(upstream.Requests);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, HttpMessageHandler upstream)
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithBearerToken();

        using HttpClient client = CreateClient(serviceProvider);

        using HttpRequestMessage request = new(method: method, requestUri: UserUri);

        return await client.SendAsync(request: request, cancellationToken: this.CancellationToken());
    }

    private static HttpClient CreateClient(ServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubSetup.HttpClientName);
    }
}
