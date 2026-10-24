using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed class GitHubRestClientTests : GitHubClientTestBase
{
    [Fact]
    public async Task GetDeserialisesResponseFromConfiguredHostAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"id":1,"name":"first"}"""));

        TestItem item = await this.CreateClient(upstream)
            .GetAsync(
                relativeUri: "repos/owner/repo",
                responseTypeInfo: TestJsonContext.Default.TestItem,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: new TestItem(Id: 1, Name: "first"), actual: item);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + "repos/owner/repo"), actual: request.RequestUri);
    }

    [Theory]
    [InlineData(
        "https://github-proxy.example/",
        "https://api.github.com/repositories/9/issues?page=2",
        "https://github-proxy.example/repositories/9/issues?page=2"
    )]
    [InlineData(
        "https://proxy.example/github/",
        "https://api.github.com/repositories/9/issues?page=2",
        "https://proxy.example/github/repositories/9/issues?page=2"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "https://ghe.example/api/v3/repositories/9/issues?page=2",
        "https://ghe.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "https://proxy.example/api/v3/",
        "https://ghe.example/api/v3/repositories/9/issues?page=2",
        "https://proxy.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "https://proxy.example/api/v3/",
        "https://api.github.com/repositories/9/issues?page=2",
        "https://proxy.example/api/v3/repositories/9/issues?page=2"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "issues?page=2",
        "https://ghe.example/api/v3/repos/owner/repo/issues?page=2"
    )]
    [InlineData("https://ghe.example/api/v3/", "?page=2", "https://ghe.example/api/v3/repos/owner/repo/issues?page=2")]
    [InlineData(
        "https://proxy.example/api/v3/",
        "https://proxy.example/api/v3//repos/o/r/issues?page=2",
        "https://proxy.example/api/v3//repos/o/r/issues?page=2"
    )]
    [InlineData(
        "https://github-proxy.example/",
        "https://github-proxy.example///x?page=2",
        "https://github-proxy.example///x?page=2"
    )]
    public async Task GetAllPagesFollowsNextLinkUnderConfiguredBaseAddressAsync(
        string baseAddress,
        string nextLink,
        string expectedSecondPage
    )
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(json: """[{"id":1,"name":"first"}]""", nextLink: $"<{nextLink}>; rel=\"next\"")
                : Responses.Json("""[{"id":2,"name":"second"}]""")
        );

        IReadOnlyList<TestItem> items = await this.CreateClient(upstream: upstream, baseAddress: baseAddress)
            .GetAllPagesAsync(
                relativeUri: "repos/owner/repo/issues",
                pageTypeInfo: TestJsonContext.Default.TestItemArray,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(
            expected: [new TestItem(Id: 1, Name: "first"), new TestItem(Id: 2, Name: "second")],
            actual: items
        );
        // Compared as text so that a rewrite such as collapsing an empty path segment is caught
        Assert.Equal<string?>(
            expected: [baseAddress + "repos/owner/repo/issues", expectedSecondPage],
            actual: [.. upstream.Requests.Select(static request => request.RequestUri?.AbsoluteUri)],
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public async Task FailureStatusThrowsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.CreateClient(upstream)
                .GetAsync(
                    relativeUri: "repos/owner/missing",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: HttpStatusCode.NotFound, actual: exception.StatusCode);
    }

    [Fact]
    public async Task ResilienceTimeoutIsAnHttpRequestFailureAsync()
    {
        TimeoutRejectedException rejection = new();
        using RecordingHttpMessageHandler upstream = new(_ => throw rejection);

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            this.CreateClient(upstream)
                .GetAsync(
                    relativeUri: "repos/owner/repo",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(
            expected: "GitHub request did not complete; it may or may not have been applied",
            actual: exception.Message
        );
        Assert.Null(exception.StatusCode);
        Assert.Same(expected: rejection, actual: exception.InnerException);
    }

    [Fact]
    public async Task NullBodyThrowsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("null"));

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.CreateClient(upstream)
                .GetAsync(
                    relativeUri: "repos/owner/repo",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );
    }

    [Theory]
    [InlineData("application/json", "<html><body>Bad gateway</body></html>")]
    [InlineData("text/html", "<html><body>Bad gateway</body></html>")]
    [InlineData("application/json", """{"id":"not a number","name":"first"}""")]
    [InlineData("application/json; charset=bogus", "<html><body>Bad gateway</body></html>")]
    public async Task UnreadableBodyIsInvalidResponseAsync(string contentType, string body)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.WithContentType(body: body, contentType: contentType)
        );

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.CreateClient(upstream)
                .GetAsync(
                    relativeUri: "repos/owner/repo",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(
            expected: "GitHub returned a response body that could not be read as JSON",
            actual: exception.Message
        );
        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Theory]
    [InlineData("https://ghe.example/api/v3/", "repos/owner/repo", "https://ghe.example/api/v3/repos/owner/repo")]
    [InlineData(
        "https://ghe.example/api/v3/",
        "/api/v3/repos/owner/repo",
        "https://ghe.example/api/v3/repos/owner/repo"
    )]
    [InlineData("https://ghe.example/api/v3/", "../v3/repos/owner/repo", "https://ghe.example/api/v3/repos/owner/repo")]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/owner/my%20repo%2ename",
        "https://ghe.example/api/v3/repos/owner/my%20repo%2ename"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "search/issues?q=repo%3Aowner%2Frepo",
        "https://ghe.example/api/v3/search/issues?q=repo%3Aowner%2Frepo"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/area%2Fapi",
        "https://ghe.example/api/v3/repos/o/r/labels/area%2Fapi"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/branches/feature%2Fx",
        "https://ghe.example/api/v3/repos/o/r/branches/feature%2Fx"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/good%20first%20issue",
        "https://ghe.example/api/v3/repos/o/r/labels/good%20first%20issue"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/100%25",
        "https://ghe.example/api/v3/repos/o/r/labels/100%25"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/a;b",
        "https://ghe.example/api/v3/repos/o/r/labels/a;b"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/what%3F",
        "https://ghe.example/api/v3/repos/o/r/labels/what%3F"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "repos/o/r/labels/issue%23",
        "https://ghe.example/api/v3/repos/o/r/labels/issue%23"
    )]
    [InlineData(
        "https://ghe.example/api/v3/",
        "search/issues?q=a;..",
        "https://ghe.example/api/v3/search/issues?q=a;.."
    )]
    public async Task UriUnderConfiguredBasePathIsRequestedAsync(
        string baseAddress,
        string relativeUri,
        string expectedUri
    )
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"id":1,"name":"first"}"""));

        await this.CreateClient(upstream: upstream, baseAddress: baseAddress)
            .GetAsync(
                relativeUri: relativeUri,
                responseTypeInfo: TestJsonContext.Default.TestItem,
                cancellationToken: this.CancellationToken()
            );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(expectedUri), actual: request.RequestUri);
    }

    [Theory]
    [InlineData(GitHubServices.ApiBaseUrl, "https://elsewhere.example/repos/owner/repo")]
    [InlineData(GitHubServices.ApiBaseUrl, "//elsewhere.example/repos/owner/repo")]
    [InlineData("https://ghe.example/api/v3/", "/repos/owner/repo")]
    [InlineData("https://ghe.example/api/v3/", "../repos/owner/repo")]
    [InlineData("https://ghe.example/api/v3/", "../../repos/owner/repo")]
    [InlineData("https://ghe.example/api/v3/", "../v3x/repos/owner/repo")]
    [InlineData("https://ghe.example/api/v3/", "..")]
    [InlineData("https://ghe.example/api/v3/", "%2e%2e/%2e%2e/admin")]
    [InlineData("https://ghe.example/api/v3/", "%2E%2e/admin")]
    [InlineData("https://ghe.example/api/v3/", ".%2e/admin")]
    [InlineData("https://ghe.example/api/v3/", "%2e./admin")]
    [InlineData("https://ghe.example/api/v3/", "repos%2f..%2f..%2fadmin")]
    [InlineData("https://ghe.example/api/v3/", "repos%5C..%5C..%5Cadmin")]
    [InlineData("https://ghe.example/api/v3/", "repos%252f..%252f..%252fadmin")]
    [InlineData("https://ghe.example/api/v3/", "repos%255c..%255c..%255cadmin")]
    [InlineData("https://ghe.example/api/v3/", "%252e%252e/admin")]
    [InlineData("https://ghe.example/api/v3/", "%25252e%25252e/admin")]
    [InlineData("https://ghe.example/api/v3/", "repos%2f.%2fadmin")]
    [InlineData("https://ghe.example/api/v3/", "repos/a%25252541")]
    [InlineData("https://ghe.example/api/v3/", "..;/admin")]
    [InlineData("https://ghe.example/api/v3/", "..;x=1/admin")]
    [InlineData("https://ghe.example/api/v3/", "%2e%2e;/admin")]
    [InlineData("https://ghe.example/api/v3/", ".;/x")]
    [InlineData("https://ghe.example/api/v3/", "repos/o/r/..%3F/..%3F/admin")]
    [InlineData("https://ghe.example/api/v3/", "..%3F/admin")]
    [InlineData("https://ghe.example/api/v3/", "..%23/admin")]
    [InlineData("https://ghe.example/api/v3/", "..%253F/admin")]
    [InlineData("https://ghe.example/api/v3/", ".%3F/x")]
    public async Task UriOffConfiguredHostOrBasePathIsRejectedAsync(string baseAddress, string relativeUri)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("{}"));

        await Assert.ThrowsAsync<UriFormatException>(() =>
            this.CreateClient(upstream: upstream, baseAddress: baseAddress)
                .GetAsync(
                    relativeUri: relativeUri,
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ClientWithoutBaseAddressIsRejectedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("{}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.CreateClient(upstream: upstream, baseAddress: null)
                .GetAsync(
                    relativeUri: "repos/owner/repo",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    // A single-resource GET has no use for a next link, so one it could not follow must not fail the request
    [Theory]
    [InlineData("<http://[>; rel=\"next\"")]
    [InlineData("<ftp://elsewhere.example/repos/owner/repo?page=2>; rel=\"next\"")]
    [InlineData("<https://ghe.example/a:b/x?page=2>; rel=\"next\"")]
    public async Task GetIgnoresAnUnusableNextLinkAsync(string nextLink)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(json: """{"id":1,"name":"first"}""", nextLink: nextLink)
        );

        TestItem item = await this.CreateClient(upstream)
            .GetAsync(
                relativeUri: "repos/owner/repo",
                responseTypeInfo: TestJsonContext.Default.TestItem,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: new TestItem(Id: 1, Name: "first"), actual: item);
        Assert.Single(upstream.Requests);
    }

    // Only the first page advertises the link, so a regression that follows it fails rather than paginating forever
    [Theory]
    [InlineData("<//elsewhere.example/repos/owner/repo/issues?page=2>; rel=\"next\"")]
    [InlineData("<ftp://elsewhere.example/repos/owner/repo/issues?page=2>; rel=\"next\"")]
    public async Task NonHttpNextLinkIsNotFollowedAsync(string nextLink)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(json: """[{"id":1,"name":"first"}]""", nextLink: nextLink)
                : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.CreateClient(upstream)
                .GetAllPagesAsync(
                    relativeUri: "repos/owner/repo/issues",
                    pageTypeInfo: TestJsonContext.Default.TestItemArray,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + "repos/owner/repo/issues"),
            actual: request.RequestUri
        );
    }

    // On the configured host the next link keeps its rooted path, so one outside the base path must not be followed
    [Fact]
    public async Task SameHostNextLinkOutsideBasePathIsNotFollowedAsync()
    {
        const string baseAddress = "https://ghe.example/api/v3/";
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Json(
                    json: """[{"id":1,"name":"first"}]""",
                    nextLink: "<https://ghe.example/repositories/9/issues?page=2>; rel=\"next\""
                )
                : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.CreateClient(upstream: upstream, baseAddress: baseAddress)
                .GetAllPagesAsync(
                    relativeUri: "repos/owner/repo/issues",
                    pageTypeInfo: TestJsonContext.Default.TestItemArray,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(baseAddress + "repos/owner/repo/issues"), actual: request.RequestUri);
    }

    [Fact]
    public async Task GetAllPagesFollowsEveryNextLinkUntilTheLastPageAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => ItemPage(id: 1, nextPage: 2),
                1 => ItemPage(id: 2, nextPage: 3),
                _ => Responses.Json("""[{"id":3,"name":"item"}]"""),
            }
        );

        IReadOnlyList<TestItem> items = await this.GetAllIssuesAsync(upstream);

        Assert.Equal(
            expected:
            [
                new TestItem(Id: 1, Name: "item"),
                new TestItem(Id: 2, Name: "item"),
                new TestItem(Id: 3, Name: "item"),
            ],
            actual: items
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    // Bounded: from the third request on there is no next link, so a regression ends rather than looping
    [Fact]
    public async Task GetAllPagesRejectsARepeatedNextLinkAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index < 2 ? ItemPage(id: index, nextPage: 2) : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() => this.GetAllIssuesAsync(upstream));

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    // Bounded: from the second request on there is no next link, so a regression ends rather than looping
    [Theory]
    [InlineData("./items?page=1", "items?page=1")]
    [InlineData("items?page=%31", "items?page=1")]
    public async Task GetAllPagesRejectsANextLinkBackToTheFirstPageWrittenDifferentlyAsync(
        string relativeUri,
        string nextLink
    )
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(json: """[{"id":1,"name":"first"}]""", nextLink: $"<{nextLink}>; rel=\"next\"")
                : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.CreateClient(upstream)
                .GetAllPagesAsync(
                    relativeUri: relativeUri,
                    pageTypeInfo: TestJsonContext.Default.TestItemArray,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + "items?page=1"), actual: request.RequestUri);
    }

    // Bounded: past the cap there is no next link, so a regression ends rather than looping
    [Fact]
    public async Task GetAllPagesStopsAtThePageCapAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index <= GitHubRestClient.MaxPages ? ItemPage(id: index, nextPage: index + 2) : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<GitHubInvalidResponseException>(() => this.GetAllIssuesAsync(upstream));

        Assert.Equal(expected: GitHubRestClient.MaxPages, actual: upstream.Requests.Count);
    }

    private async Task<IReadOnlyList<TestItem>> GetAllIssuesAsync(HttpMessageHandler upstream)
    {
        return await this.CreateClient(upstream)
            .GetAllPagesAsync(
                relativeUri: "repos/owner/repo/issues",
                pageTypeInfo: TestJsonContext.Default.TestItemArray,
                cancellationToken: this.CancellationToken()
            );
    }

    private static HttpResponseMessage ItemPage(int id, int nextPage)
    {
        return Responses.Json(
            json: $$"""[{"id":{{id}},"name":"item"}]""",
            nextLink: $"<{GitHubServices.ApiBaseUrl}repos/owner/repo/issues?page={nextPage}>; rel=\"next\""
        );
    }

    private GitHubRestClient CreateClient(HttpMessageHandler upstream, string? baseAddress = GitHubServices.ApiBaseUrl)
    {
        return new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream: upstream, baseAddress: baseAddress),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
    }
}
