using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.ReleaseView;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.ReleaseView;

public sealed class ReleaseServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string TAG = "v1.2.3";
    private const string PUBLISHED_AT = "2026-11-17T09:39:14Z";
    private const string URL = "https://github.com/credfeto/credfeto-workflow-mcp/releases/tag/v1.2.3";
    private const string NAME = "Release 1.2.3";
    private const string FAILED = "could not read the latest release of credfeto/credfeto-workflow-mcp";

    private static string ReleaseJson(string? tag = TAG, string? name = NAME, string? publishedAt = PUBLISHED_AT)
    {
        return "{\"tag_name\":"
            + JsonText(tag)
            + ",\"published_at\":"
            + JsonText(publishedAt)
            + ",\"html_url\":\""
            + URL
            + "\",\"name\":"
            + JsonText(name)
            + ",\"body\":\"release notes that are never read\"}";
    }

    private static string JsonText(string? value)
    {
        return value is null ? "null" : "\"" + value + "\"";
    }

    private static Uri ApiUri(string suffix)
    {
        return new(GitHubServices.ApiBaseUrl + suffix);
    }

    private static Uri LatestReleaseUri()
    {
        return ApiUri("repos/" + REPOSITORY + "/releases/latest");
    }

    private static Uri RepositoryUri()
    {
        return ApiUri("repos/" + REPOSITORY);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static Uri?[] RequestUris(RecordingHttpMessageHandler upstream)
    {
        return [.. upstream.Requests.Select(request => request.RequestUri)];
    }

    private static RecordingHttpMessageHandler Answers(string json)
    {
        return new(index => index == 0 ? Responses.Json(json) : Unexpected(index));
    }

    private ReleaseService CreateService(RecordingHttpMessageHandler upstream)
    {
        IHttpClientFactory httpClientFactory = MockHttpClientFactoryCreateClient(upstream);
        GitHubRestClient rest = new(
            httpClientFactory: httpClientFactory,
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        GitHubGraphQlClient graphQl = new(
            httpClientFactory: httpClientFactory,
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(restClient: rest, graphQlClient: graphQl);
    }

    private async Task<ReleaseViewResult> ViewLatestAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ViewLatestAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<ReleaseViewFailedException> ViewFailsAsync(RecordingHttpMessageHandler upstream)
    {
        ReleaseViewFailedException exception = await Assert.ThrowsAsync<ReleaseViewFailedException>(() =>
            this.ViewLatestAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    [Fact]
    public async Task ReadsTheLatestReleaseWithANameAsync()
    {
        using RecordingHttpMessageHandler upstream = Answers(ReleaseJson());

        ReleaseViewResult result = await this.ViewLatestAsync(upstream);

        Assert.Equal(expected: new(Tag: TAG, PublishedAt: PUBLISHED_AT, Url: new(URL), Name: NAME), actual: result);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: LatestReleaseUri(), actual: request.RequestUri);
    }

    [Fact]
    public async Task AReleaseWithoutANameHasAnEmptyNameAsync()
    {
        using RecordingHttpMessageHandler upstream = Answers(ReleaseJson(name: null));

        ReleaseViewResult result = await this.ViewLatestAsync(upstream);

        Assert.Equal(expected: new(Tag: TAG, PublishedAt: PUBLISHED_AT, Url: new(URL), Name: ""), actual: result);
    }

    [Fact]
    public async Task AReleaseWithAnEmptyNameHasAnEmptyNameAsync()
    {
        using RecordingHttpMessageHandler upstream = Answers(ReleaseJson(name: ""));

        ReleaseViewResult result = await this.ViewLatestAsync(upstream);

        Assert.Equal(expected: "", actual: result.Name);
    }

    [Fact]
    public async Task AMissingPublishedAtIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = Answers(ReleaseJson(publishedAt: null));

        ReleaseViewResult result = await this.ViewLatestAsync(upstream);

        Assert.Equal(expected: new(Tag: TAG, PublishedAt: "", Url: new(URL), Name: NAME), actual: result);
    }

    [Fact]
    public async Task ControlCharactersInTheTagNameAndDateBecomeSpacesAsync()
    {
        using RecordingHttpMessageHandler upstream = Answers(
            ReleaseJson(tag: "v1\\n2\\u001b3", name: "line one\\r\\nline two\\u2028", publishedAt: "2026\\t11")
        );

        ReleaseViewResult result = await this.ViewLatestAsync(upstream);

        Assert.Equal(expected: "v1 2 3", actual: result.Tag);
        Assert.Equal(expected: "line one  line two ", actual: result.Name);
        Assert.Equal(expected: "2026 11", actual: result.PublishedAt);
    }

    [Theory]
    [InlineData("{\"name\":\"" + NAME + "\",\"html_url\":\"" + URL + "\"}")]
    [InlineData("{\"tag_name\":null,\"html_url\":\"" + URL + "\"}")]
    [InlineData("{\"tag_name\":\"\",\"html_url\":\"" + URL + "\"}")]
    [InlineData("{\"tag_name\":\"" + TAG + "\"}")]
    [InlineData("{\"tag_name\":\"" + TAG + "\",\"html_url\":null}")]
    [InlineData("{\"tag_name\":\"" + TAG + "\",\"html_url\":\"releases/tag/v1.2.3\"}")]
    [InlineData("{\"tag_name\":\"" + TAG + "\",\"html_url\":\"ftp://github.com/releases/tag/v1.2.3\"}")]
    [InlineData("[]")]
    public async Task AReleaseWithoutATagOrAValidLinkIsAFailedReadAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Answers(json);

        ReleaseViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task NoLatestReleaseInARepositoryThatExistsIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Status(HttpStatusCode.NotFound),
                1 => Responses.Json("{\"full_name\":\"" + REPOSITORY + "\",\"default_branch\":\"main\"}"),
                _ => Unexpected(index),
            }
        );

        ReleaseNotFoundException exception = await Assert.ThrowsAsync<ReleaseNotFoundException>(() =>
            this.ViewLatestAsync(upstream)
        );

        Assert.Equal(expected: REPOSITORY + " has no latest release", actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
        Assert.Equal(expected: [LatestReleaseUri(), RepositoryUri()], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task AMissingRepositoryIsReportedAsSuchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index < 2 ? Responses.Status(HttpStatusCode.NotFound) : Unexpected(index)
        );

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ViewLatestAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: [LatestReleaseUri(), RepositoryUri()], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task AFailedRepositoryCheckIsAFailedReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Status(HttpStatusCode.NotFound),
                _ => Responses.Status(HttpStatusCode.Unauthorized),
            }
        );

        ReleaseViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Unauthorized, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedReleaseReadIsAFailedReadAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(statusCode));

        ReleaseViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: statusCode, actual: inner.StatusCode);
        Assert.All(collection: RequestUris(upstream), action: uri =>
            Assert.Equal(expected: LatestReleaseUri(), actual: uri)
        );
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/a/b")]
    [InlineData("credfeto/..")]
    [InlineData("credfeto/repo?x=1")]
    [InlineData("")]
    public async Task AnInvalidRepositoryIsRefusedWithoutAReadAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ViewLatestAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
