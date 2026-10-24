using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
    }

    [Theory]
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
    public async Task NonObjectResponseIsInvalidAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("[]"));

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() => this.ExecuteAsync(upstream).AsTask());
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

        Assert.Equal(expected: "GitHub request was rejected before a response was received", actual: exception.Message);
        Assert.Null(exception.StatusCode);
        Assert.Same(expected: rejection, actual: exception.InnerException);
    }

    private ValueTask<TestData> ExecuteAsync(
        HttpMessageHandler upstream,
        string baseAddress = GitHubServices.ApiBaseUrl
    )
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream: upstream, baseAddress: baseAddress),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return client.ExecuteAsync(
            query: QUERY,
            variables: new TestVariables(Owner: "owner", Number: 42),
            variablesTypeInfo: TestJsonContext.Default.TestVariables,
            dataTypeInfo: TestJsonContext.Default.TestData,
            cancellationToken: this.CancellationToken()
        );
    }
}
