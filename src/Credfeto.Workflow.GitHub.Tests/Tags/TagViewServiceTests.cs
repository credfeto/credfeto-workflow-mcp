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
using Credfeto.Workflow.GitHub.Tags;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Tags;

public sealed class TagViewServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string TAG = "v1";
    private const string COMMIT_SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string TAG_SHA = "fedcba9876543210fedcba9876543210fedcba98";
    private const string FAILED = "could not read the tag v1 in credfeto/credfeto-workflow-mcp";
    private const string NOT_COMMIT =
        "the tag v1 in credfeto/credfeto-workflow-mcp does not point to a commit, itself or through one annotated tag";

    private static string RefJson(string type, string sha, string gitRef = "refs/tags/" + TAG)
    {
        return "{\"ref\":\"" + gitRef + "\",\"object\":" + ObjectJson(type: type, sha: sha) + "}";
    }

    private static string TagJson(string type, string sha)
    {
        return "{\"object\":" + ObjectJson(type: type, sha: sha) + "}";
    }

    private static string ObjectJson(string type, string sha)
    {
        return "{\"type\":\"" + type + "\",\"sha\":\"" + sha + "\"}";
    }

    private static Uri ApiUri(string suffix)
    {
        return new(GitHubServices.ApiBaseUrl + suffix);
    }

    private static Uri RefUri(string encodedTag)
    {
        return ApiUri("repos/" + REPOSITORY + "/git/ref/tags/" + encodedTag);
    }

    private static Uri TagObjectUri(string sha)
    {
        return ApiUri("repos/" + REPOSITORY + "/git/tags/" + sha);
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

    private TagViewService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<TagViewResponse> ViewAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string tag = TAG
    )
    {
        return await this.CreateService(upstream)
            .ViewAsync(repository: repository, tag: tag, cancellationToken: this.CancellationToken());
    }

    private static RecordingHttpMessageHandler Annotated(string tagObjectJson)
    {
        return new(index =>
            index switch
            {
                0 => Responses.Json(RefJson(type: "tag", sha: TAG_SHA)),
                1 => Responses.Json(tagObjectJson),
                _ => Unexpected(index),
            }
        );
    }

    private async Task<TagViewFailedException> ViewFailsAsync(RecordingHttpMessageHandler upstream)
    {
        TagViewFailedException exception = await Assert.ThrowsAsync<TagViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    [Fact]
    public async Task ReadsALightweightTagAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RefJson(type: "commit", sha: COMMIT_SHA)) : Unexpected(index)
        );

        TagViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: new(Sha: COMMIT_SHA, Type: TagType.Lightweight), actual: response);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: RefUri(TAG), actual: request.RequestUri);
    }

    [Fact]
    public async Task ReadsTheCommitOfAnAnnotatedTagAsync()
    {
        using RecordingHttpMessageHandler upstream = Annotated(TagJson(type: "commit", sha: COMMIT_SHA));

        TagViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: new(Sha: COMMIT_SHA, Type: TagType.Annotated), actual: response);
        Assert.Equal(expected: [RefUri(TAG), TagObjectUri(TAG_SHA)], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task ATagOfATagIsNotFollowedAsync()
    {
        using RecordingHttpMessageHandler upstream = Annotated(TagJson(type: "tag", sha: COMMIT_SHA));

        TagViewFailedException exception = await Assert.ThrowsAsync<TagViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: NOT_COMMIT, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("blob")]
    public async Task AnAnnotatedTagOfAnythingButACommitFailsAsync(string type)
    {
        using RecordingHttpMessageHandler upstream = Annotated(TagJson(type: type, sha: COMMIT_SHA));

        TagViewFailedException exception = await Assert.ThrowsAsync<TagViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: NOT_COMMIT, actual: exception.Message);
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("blob")]
    public async Task ALightweightTagOfAnythingButACommitFailsAsync(string type)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RefJson(type: type, sha: COMMIT_SHA)) : Unexpected(index)
        );

        TagViewFailedException exception = await Assert.ThrowsAsync<TagViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: NOT_COMMIT, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("refs/heads/v1")]
    [InlineData("refs/tags/v1.0")]
    [InlineData("refs/tags/V1")]
    [InlineData("v1")]
    public async Task ARefOtherThanTheTagIsUnexpectedAsync(string gitRef)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RefJson(type: "commit", sha: COMMIT_SHA, gitRef: gitRef)) : Unexpected(index)
        );

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("{\"object\":{\"type\":\"commit\",\"sha\":\"" + COMMIT_SHA + "\"}}")]
    [InlineData("{\"ref\":\"refs/tags/v1\"}")]
    [InlineData("{\"ref\":\"refs/tags/v1\",\"object\":{\"sha\":\"" + COMMIT_SHA + "\"}}")]
    [InlineData("{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"\",\"sha\":\"" + COMMIT_SHA + "\"}}")]
    [InlineData("{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"commit\"}}")]
    [InlineData(
        "{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"commit\",\"sha\":\"0123456789ABCDEF0123456789abcdef01234567\"}}"
    )]
    [InlineData("{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"commit\",\"sha\":\"0123456789abcdef\"}}")]
    [InlineData(
        "{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"commit\",\"sha\":\"0123456789abcdef0123456789abcdef012345678\"}}"
    )]
    [InlineData(
        "{\"ref\":\"refs/tags/v1\",\"object\":{\"type\":\"commit\",\"sha\":\"0123456789abcdef0123456789abcdef0123456g\"}}"
    )]
    [InlineData("[]")]
    public async Task AnInvalidRefIsUnexpectedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(json) : Unexpected(index)
        );

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"object\":{\"sha\":\"" + COMMIT_SHA + "\"}}")]
    [InlineData("{\"object\":{\"type\":\"commit\"}}")]
    [InlineData("{\"object\":{\"type\":\"commit\",\"sha\":\"0123456789ABCDEF0123456789abcdef01234567\"}}")]
    [InlineData("{\"object\":{\"type\":\"commit\",\"sha\":\"0123456789abcdef\"}}")]
    public async Task AnInvalidTagObjectIsUnexpectedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Annotated(json);

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMissingTagObjectIsUnexpectedRatherThanNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(RefJson(type: "tag", sha: TAG_SHA)),
                1 => Responses.Status(HttpStatusCode.NotFound),
                _ => Unexpected(index),
            }
        );

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
        Assert.Equal(expected: [RefUri(TAG), TagObjectUri(TAG_SHA)], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task AMissingTagInARepositoryThatExistsIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Status(HttpStatusCode.NotFound),
                1 => Responses.Json("{\"full_name\":\"" + REPOSITORY + "\",\"default_branch\":\"main\"}"),
                _ => Unexpected(index),
            }
        );

        TagNotFoundException exception = await Assert.ThrowsAsync<TagNotFoundException>(() => this.ViewAsync(upstream));

        Assert.Equal(expected: "the tag v1 was not found in " + REPOSITORY, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
        Assert.Equal(expected: [RefUri(TAG), RepositoryUri()], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task AMissingRepositoryIsReportedAsSuchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index < 2 ? Responses.Status(HttpStatusCode.NotFound) : Unexpected(index)
        );

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: [RefUri(TAG), RepositoryUri()], actual: RequestUris(upstream));
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

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Unauthorized, actual: inner.StatusCode);
    }

    [Fact]
    public async Task AFailedRefReadIsAFailedReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.Unauthorized));

        TagViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Unauthorized, actual: inner.StatusCode);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task EncodesSpecialCharactersInTheTagAsync()
    {
        const string tag = "release/v1+b#c%d&e";

        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(RefJson(type: "commit", sha: COMMIT_SHA, gitRef: "refs/tags/" + tag))
                : Unexpected(index)
        );

        TagViewResponse response = await this.ViewAsync(upstream: upstream, tag: tag);

        Assert.Equal(expected: new(Sha: COMMIT_SHA, Type: TagType.Lightweight), actual: response);
        Assert.Equal(
            expected: RefUri("release/v1%2Bb%23c%25d%26e").AbsoluteUri,
            actual: Assert.Single(upstream.Requests).RequestUri?.AbsoluteUri
        );
    }

    [Theory]
    [InlineData("credfeto", TAG, "repository")]
    [InlineData("credfeto/a/b", TAG, "repository")]
    [InlineData(REPOSITORY, "", "tag")]
    [InlineData(REPOSITORY, "v1..2", "tag")]
    [InlineData(REPOSITORY, "v1\n", "tag")]
    [InlineData(REPOSITORY, "v1 2", "tag")]
    [InlineData(REPOSITORY, "v1.lock", "tag")]
    public async Task InvalidArgumentsAreRefusedWithoutAReadAsync(string repository, string tag, string parameterName)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ViewAsync(upstream: upstream, repository: repository, tag: tag)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
