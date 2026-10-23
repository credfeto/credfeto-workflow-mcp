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
        Assert.Equal(
            expected: [new Uri(baseAddress + "repos/owner/repo/issues"), new Uri(expectedSecondPage)],
            actual: [.. upstream.Requests.Select(static request => request.RequestUri)]
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
    [InlineData(GitHubServices.ApiBaseUrl, "https://elsewhere.example/repos/owner/repo")]
    [InlineData(GitHubServices.ApiBaseUrl, "//elsewhere.example/repos/owner/repo")]
    [InlineData(null, "repos/owner/repo")]
    public async Task UriOffConfiguredHostOrWithoutBaseAddressIsRejectedAsync(string? baseAddress, string relativeUri)
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

    // Only the first page advertises the link, so a regression that follows it fails rather than paginating forever
    [Fact]
    public async Task NetworkPathNextLinkIsNotFollowedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Json(
                    json: """[{"id":1,"name":"first"}]""",
                    nextLink: "<//elsewhere.example/repos/owner/repo/issues?page=2>; rel=\"next\""
                )
                : Responses.Json("[]")
        );

        await Assert.ThrowsAsync<UriFormatException>(() =>
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
