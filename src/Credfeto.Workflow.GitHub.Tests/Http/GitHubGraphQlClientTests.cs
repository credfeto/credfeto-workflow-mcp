using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Models;
using Polly.Timeout;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class GitHubGraphQlClientTests : GitHubClientTestBase
{
    private const string QUERY = "query($owner: String!, $number: Int!) { viewer { login } }";

    [Theory]
    [InlineData(GitHubServices.ApiBaseUrl, GitHubServices.ApiBaseUrl + "graphql")]
    [InlineData("https://ghe.example/api/v3/", "https://ghe.example/api/graphql")]
    public async Task ExecutePostsQueryAndVariablesToGraphQlEndpointAsync(string baseAddress, string expectedUri)
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"login":"octocat"}}""")
        );

        TestData data = await this.ExecuteAsync(upstream: upstream, baseAddress: baseAddress);

        Assert.Equal(expected: new TestData("octocat"), actual: data);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: new Uri(expectedUri), actual: request.RequestUri);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        Assert.Equal(expected: QUERY, actual: body.RootElement.GetProperty("query").GetString());
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "owner", actual: variables.GetProperty("owner").GetString());
        Assert.Equal(expected: 42, actual: variables.GetProperty("number").GetInt32());
    }

    [Theory]
    [InlineData("https://proxy.example/ghe/", "/api/v3/", "https://proxy.example/ghe/api/graphql")]
    [InlineData("https://proxy.example/ghe/", "/", "https://proxy.example/ghe/graphql")]
    [InlineData("https://proxy.example/ghe/", "/ghe/", "https://proxy.example/ghe/graphql")]
    [InlineData(
        "https://proxy.example/ghe/",
        "/enterprise/api/v3/",
        "https://proxy.example/ghe/enterprise/api/graphql"
    )]
    [InlineData("https://ghe.example/api/v3/", "/api/v3/", "https://ghe.example/api/graphql")]
    [InlineData("https://proxy.example/p/api/v3/", "/api/v3/", "https://proxy.example/p/api/graphql")]
    [InlineData("https://proxy.example/api/v3/", "/", "https://proxy.example/api/v3/graphql")]
    [InlineData("https://proxy.example/x/", "/api/", "https://proxy.example/x/api/graphql")]
    public async Task UpstreamBasePathMountsTheUpstreamGraphQlEndpointUnderTheProxyAsync(
        string baseAddress,
        string upstreamBasePath,
        string expectedUri
    )
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"login":"octocat"}}""")
        );

        TestData data = await this.ExecuteAsync(
            upstream: upstream,
            dataTypeInfo: TestJsonContext.Default.TestData,
            baseAddress: baseAddress,
            upstreamBasePath: upstreamBasePath
        );

        Assert.Equal(expected: new TestData("octocat"), actual: data);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(expectedUri), actual: request.RequestUri);
    }

    [Fact]
    public async Task EmptyErrorsArrayIsNotAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"login":"octocat"},"errors":[]}""")
        );

        TestData data = await this.ExecuteAsync(upstream);

        Assert.Equal(expected: new TestData("octocat"), actual: data);
    }

    [Fact]
    public async Task ErrorsAreSurfacedEvenWithPartialDataAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"login":"octocat"},"errors":[{"message":"Could not resolve to a Repository"},{"type":"FORBIDDEN"},"bad"]}"""
            )
        );

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "GitHub GraphQL request returned errors", actual: exception.Message);
        Assert.Equal(
            expected: ["Could not resolve to a Repository", "unknown error", "unknown error"],
            actual: exception.Errors
        );
        Assert.Equal(expected: ["FORBIDDEN"], actual: exception.ErrorTypes);
    }

    [Fact]
    public async Task ErrorTypesAreSurfacedInOrderAndOnlyWhenTheyAreStringsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"errors":[{"type":"INSUFFICIENT_SCOPES","message":"a"},{"type":1,"message":"b"},{"type":"NOT_FOUND"}],"data":null}"""
            )
        );

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: ["INSUFFICIENT_SCOPES", "NOT_FOUND"], actual: exception.ErrorTypes);
    }

    [Fact]
    public async Task ErrorsAreSurfacedWhenDataIsNullAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Not found"}],"data":null}""")
        );

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: ["Not found"], actual: exception.Errors);
    }

    [Theory]
    [InlineData("""{"data":{"login":"octocat"},"errors":{"message":"Something went wrong"}}""")]
    [InlineData("""{"data":{"login":"octocat"},"errors":"Something went wrong"}""")]
    [InlineData("""{"data":{"login":"octocat"},"errors":1}""")]
    [InlineData("""{"data":{"login":"octocat"},"errors":true}""")]
    public async Task NonArrayErrorsAreInvalidResponseAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "GitHub GraphQL response errors value was not an array", actual: exception.Message);
    }

    [Fact]
    public async Task NullBodyIsInvalidResponseAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("null"));

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "GitHub GraphQL response body was null", actual: exception.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"errors":null}""")]
    [InlineData("""{"data":null}""")]
    public async Task MissingDataIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Empty(exception.Errors);
    }

    [Fact]
    public async Task ErrorsAreSurfacedWhenPartialDataDoesNotMatchTheDataTypeAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"login":123},"errors":[{"message":"Could not resolve to a Repository"}]}""")
        );

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: ["Could not resolve to a Repository"], actual: exception.Errors);
    }

    [Fact]
    public async Task ValueTypeDataIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"data":{"count":3}}"""));

        TestCount data = await this.ExecuteAsync(upstream: upstream, dataTypeInfo: TestJsonContext.Default.TestCount);

        Assert.Equal(expected: new TestCount(3), actual: data);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    public async Task MissingValueTypeDataIsAFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            this.ExecuteAsync(upstream: upstream, dataTypeInfo: TestJsonContext.Default.TestCount).AsTask()
        );

        Assert.Equal(expected: "GitHub GraphQL response contained no data", actual: exception.Message);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public async Task NonObjectResponseIsInvalidAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("[]"));

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Theory]
    [InlineData(true, "<html><body>Bad gateway</body></html>")]
    [InlineData(false, """{"data":"not an object"}""")]
    public async Task UnreadableBodyIsInvalidResponseAsync(bool asHtml, string body)
    {
        using RecordingHttpMessageHandler upstream = new(_ => asHtml ? Responses.Html(body) : Responses.Json(body));

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "GitHub GraphQL response body could not be read as JSON", actual: exception.Message);
        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task FailureStatusThrowsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: HttpStatusCode.BadGateway, actual: exception.StatusCode);
    }

    [Fact]
    public async Task ResilienceTimeoutIsAnHttpRequestFailureAsync()
    {
        TimeoutRejectedException rejection = new();
        using RecordingHttpMessageHandler upstream = new(_ => throw rejection);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(
            expected: "GitHub request did not complete; it may or may not have been applied",
            actual: exception.Message
        );
        Assert.Null(exception.StatusCode);
        Assert.Same(expected: rejection, actual: exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect, GitHubServices.ApiBaseUrl, GitHubServices.ApiBaseUrl + "graphql2")]
    [InlineData(HttpStatusCode.PermanentRedirect, GitHubServices.ApiBaseUrl, "/graphql?x=1")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "https://ghe.example/api/v3/", "https://ghe.example/api/graphql2")]
    [InlineData(HttpStatusCode.PermanentRedirect, "https://ghe.example/api/v3/", "/api/v3/graphql")]
    public async Task RedirectThatKeepsTheMethodReplaysTheQueryAsync(
        HttpStatusCode statusCode,
        string baseAddress,
        string location
    )
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Redirect(statusCode: statusCode, location: location)
                : Responses.Json("""{"data":{"login":"octocat"}}""")
        );

        TestData data = await this.ExecuteAsync(upstream: upstream, baseAddress: baseAddress);

        Assert.Equal(expected: new TestData("octocat"), actual: data);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        RecordedRequest original = upstream.Requests[0];
        RecordedRequest redirected = upstream.Requests[1];
        Assert.Equal(expected: HttpMethod.Post, actual: redirected.Method);
        Assert.Equal(expected: new Uri(new Uri(baseAddress), location), actual: redirected.RequestUri);
        Assert.Equal(expected: original.Body, actual: redirected.Body);

        using JsonDocument body = JsonDocument.Parse(redirected.Body ?? string.Empty);
        Assert.Equal(expected: QUERY, actual: body.RootElement.GetProperty("query").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    public async Task RedirectThatWouldChangeTheMethodIsNotFollowedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Redirect(statusCode: statusCode, location: GitHubServices.ApiBaseUrl + "graphql2")
        );

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.ExecuteAsync(upstream).AsTask()
        );

        Assert.Equal(expected: statusCode, actual: exception.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(GitHubServices.ApiBaseUrl, "https://elsewhere.example/graphql")]
    [InlineData("https://ghe.example/api/v3/", "https://ghe.example/graphql")]
    [InlineData("https://ghe.example/api/v3/", "/admin")]
    [InlineData("https://ghe.example/api/v3/", "../graphql")]
    public async Task RedirectOutsideTheGraphQlScopeIsNotFollowedAsync(string baseAddress, string location)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Redirect(statusCode: HttpStatusCode.TemporaryRedirect, location: location)
        );

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.ExecuteAsync(upstream: upstream, baseAddress: baseAddress).AsTask()
        );

        Assert.Equal(expected: HttpStatusCode.TemporaryRedirect, actual: exception.StatusCode);
        Assert.DoesNotContain(
            expectedSubstring: location,
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Single(upstream.Requests);
    }

    private ValueTask<TestData> ExecuteAsync(
        HttpMessageHandler upstream,
        string baseAddress = GitHubServices.ApiBaseUrl
    )
    {
        return this.ExecuteAsync(
            upstream: upstream,
            dataTypeInfo: TestJsonContext.Default.TestData,
            baseAddress: baseAddress
        );
    }

    private ValueTask<TData> ExecuteAsync<TData>(
        HttpMessageHandler upstream,
        JsonTypeInfo<TData> dataTypeInfo,
        string baseAddress = GitHubServices.ApiBaseUrl,
        string? upstreamBasePath = null
    )
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream: upstream, baseAddress: baseAddress),
            options: GitHubOptionsWith(upstreamBasePath),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return client.ExecuteAsync(
            query: QUERY,
            variables: new TestVariables(Owner: "owner", Number: 42),
            variablesTypeInfo: TestJsonContext.Default.TestVariables,
            dataTypeInfo: dataTypeInfo,
            cancellationToken: this.CancellationToken()
        );
    }
}
