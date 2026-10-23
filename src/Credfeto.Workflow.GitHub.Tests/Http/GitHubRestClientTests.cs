using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

    [Fact]
    public async Task AbsoluteUriIsRejectedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("{}"));

        await Assert.ThrowsAsync<UriFormatException>(() =>
            this.CreateClient(upstream)
                .GetAsync(
                    relativeUri: "https://elsewhere.example/repos/owner/repo",
                    responseTypeInfo: TestJsonContext.Default.TestItem,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    private GitHubRestClient CreateClient(HttpMessageHandler upstream)
    {
        return this.CreateClient(upstream: upstream, baseAddress: GitHubServices.ApiBaseUrl);
    }

    private GitHubRestClient CreateClient(HttpMessageHandler upstream, string baseAddress)
    {
        return new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream: upstream, baseAddress: baseAddress),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
    }
}
