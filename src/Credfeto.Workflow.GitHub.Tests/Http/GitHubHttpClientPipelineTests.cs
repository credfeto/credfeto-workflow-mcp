using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Models;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubHttpClientPipelineTests : TestBase
{
    private const int RETRY_ATTEMPTS = 3;

    // Far smaller than OVERSIZED_JSON, so buffering it exceeds the limit
    private const long SMALL_BUFFER_LIMIT = 8;

    private const string OVERSIZED_JSON = """{"name":"far longer than the buffer limit allows"}""";

    // Well beyond the retry delay cap, like the minute GitHub can ask for on a secondary rate limit
    private static readonly TimeSpan LongRetryAfter = TimeSpan.FromSeconds(60);

    private static readonly Uri UserUri = new(uriString: "user", uriKind: UriKind.Relative);

    // Short enough to keep the tests fast, and far inside the resilience attempt timeout so only HttpClient fires
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task ClientIsConfiguredForGitHubAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        using HttpClient client = CreateClient(serviceProvider);

        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl), actual: client.BaseAddress);
        Assert.Equal(expected: TimeSpan.FromSeconds(30), actual: client.Timeout);
        Assert.Equal(expected: 32 * 1024 * 1024, actual: client.MaxResponseContentBufferSize);
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

    [Fact]
    public async Task PrimaryHandlerDoesNotFollowRedirectsAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        SocketsHttpHandler primary = GetPrimaryHandler(serviceProvider);

        Assert.False(condition: primary.AllowAutoRedirect, userMessage: "Redirects must not be followed");
    }

    [Fact]
    public async Task PrimaryHandlerDoesNotShareCookiesBetweenCallersAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        SocketsHttpHandler primary = GetPrimaryHandler(serviceProvider);

        Assert.False(condition: primary.UseCookies, userMessage: "Cookies must not be shared between callers");
    }

    [Fact]
    public async Task PrimaryHandlerDecompressesResponsesAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        SocketsHttpHandler primary = GetPrimaryHandler(serviceProvider);

        Assert.Equal(expected: DecompressionMethods.All, actual: primary.AutomaticDecompression);
    }

    [Theory]
    [InlineData("POST", 1)]
    [InlineData("GET", RETRY_ATTEMPTS + 1)]
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

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RetryAfterBeyondTheDelayCapIsReturnedWithoutRetryingAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithRetryAfter(statusCode: statusCode, retryAfter: new(LongRetryAfter))
        );

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Get, upstream: upstream);

        Assert.Equal(expected: statusCode, actual: response.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RetryAfterDateBeyondTheDelayCapIsReturnedWithoutRetryingAsync()
    {
        DateTimeOffset retryAt = TimeProvider.System.GetUtcNow().Add(LongRetryAfter);
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithRetryAfter(statusCode: HttpStatusCode.TooManyRequests, retryAfter: new(retryAt))
        );

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Get, upstream: upstream);

        Assert.Equal(expected: HttpStatusCode.TooManyRequests, actual: response.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RetryAfterWithinTheDelayCapIsRetriedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.StatusWithRetryAfter(statusCode: HttpStatusCode.TooManyRequests, retryAfter: new(TimeSpan.Zero))
        );

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Get, upstream: upstream);

        Assert.Equal(expected: HttpStatusCode.TooManyRequests, actual: response.StatusCode);
        Assert.Equal(expected: RETRY_ATTEMPTS + 1, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task RetryAfterDateWithinTheDelayCapIsRetriedAsync()
    {
        DateTimeOffset retryAt = TimeProvider.System.GetUtcNow();
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithRetryAfter(statusCode: HttpStatusCode.TooManyRequests, retryAfter: new(retryAt))
        );

        using HttpResponseMessage response = await this.SendAsync(method: HttpMethod.Get, upstream: upstream);

        Assert.Equal(expected: HttpStatusCode.TooManyRequests, actual: response.StatusCode);
        Assert.Equal(expected: RETRY_ATTEMPTS + 1, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task RateLimitBeyondTheDelayCapKeepsItsStatusForTheCallerAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.StatusWithRetryAfter(statusCode: HttpStatusCode.TooManyRequests, retryAfter: new(LongRetryAfter))
        );

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.GetThroughRestClientAsync(upstream)
        );

        Assert.Equal(expected: HttpStatusCode.TooManyRequests, actual: exception.StatusCode);
    }

    [Fact]
    public async Task AttemptTimeoutIsRetriedThenSurfacedAsARejectionAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => throw new TimeoutRejectedException());

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.GetThroughRestClientAsync(upstream)
        );

        Assert.Equal(expected: "GitHub request was rejected before a response was received", actual: exception.Message);
        Assert.Null(exception.StatusCode);
        Assert.IsType<TimeoutRejectedException>(exception.InnerException);
        Assert.Equal(expected: RETRY_ATTEMPTS + 1, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task HttpClientTimeoutWhileBufferingTheBodyIsSurfacedAsARejectionAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => StalledBody());
        await using ServiceProvider serviceProvider = GitHubServices.Build(
            primaryHandler: upstream,
            httpClientTimeout: ShortTimeout
        );

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            GetThroughRestClientAsync(serviceProvider: serviceProvider, cancellationToken: this.CancellationToken())
        );

        Assert.Equal(expected: "GitHub request was rejected before a response was received", actual: exception.Message);
        Assert.Null(exception.StatusCode);
        OperationCanceledException timeout = Assert.IsAssignableFrom<OperationCanceledException>(
            exception.InnerException
        );
        Assert.IsType<TimeoutException>(timeout.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BodyLargerThanTheBufferLimitFailsTheRequestAsync(bool sized)
    {
        using RecordingHttpMessageHandler upstream = new(_ => OversizedBody(sized));
        await using ServiceProvider serviceProvider = GitHubServices.Build(
            primaryHandler: upstream,
            maxResponseContentBufferSize: SMALL_BUFFER_LIMIT
        );

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            GetThroughRestClientAsync(serviceProvider: serviceProvider, cancellationToken: this.CancellationToken())
        );

        Assert.Null(exception.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CallerCancellationWhileBufferingTheBodyIsNotARejectionAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => StalledBody());
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            this.CancellationToken()
        );
        cancellation.CancelAfter(ShortTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GetThroughRestClientAsync(serviceProvider: serviceProvider, cancellationToken: cancellation.Token)
        );
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, HttpMessageHandler upstream)
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithBearerToken();

        using HttpClient client = CreateClient(serviceProvider);

        using HttpRequestMessage request = new(method: method, requestUri: UserUri);

        return await client.SendAsync(request: request, cancellationToken: this.CancellationToken());
    }

    private async Task<TestItem> GetThroughRestClientAsync(HttpMessageHandler upstream)
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream);

        return await GetThroughRestClientAsync(
            serviceProvider: serviceProvider,
            cancellationToken: this.CancellationToken()
        );
    }

    // A sized body is rejected from its Content-Length before it is read; an unsized one, as a decompressed body
    // is, only once the bytes read pass the limit
    private static HttpResponseMessage OversizedBody(bool sized)
    {
        return sized
            ? Responses.Json(OVERSIZED_JSON)
            : new(HttpStatusCode.OK) { Content = new UnsizedContent(Encoding.UTF8.GetBytes(OVERSIZED_JSON)) };
    }

    private static HttpResponseMessage StalledBody()
    {
        return new(HttpStatusCode.OK) { Content = new StallingContent() };
    }

    private static async Task<TestItem> GetThroughRestClientAsync(
        ServiceProvider serviceProvider,
        CancellationToken cancellationToken
    )
    {
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithBearerToken();

        return await serviceProvider
            .GetRequiredService<IGitHubRestClient>()
            .GetAsync(
                relativeUri: "user",
                responseTypeInfo: TestJsonContext.Default.TestItem,
                cancellationToken: cancellationToken
            );
    }

    private static SocketsHttpHandler GetPrimaryHandler(ServiceProvider serviceProvider)
    {
        HttpMessageHandler handler = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(GitHubSetup.HttpClientName);

        while (handler is DelegatingHandler delegatingHandler)
        {
            Assert.NotNull(delegatingHandler.InnerHandler);
            handler = delegatingHandler.InnerHandler;
        }

        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    private static HttpClient CreateClient(ServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubSetup.HttpClientName);
    }
}
