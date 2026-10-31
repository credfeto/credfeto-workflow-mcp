using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class BoardWriterTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string REPO = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + REPO;
    private const int NUMBER = 42;
    private const string PROJECT_ID = "PVT_kwDOA";
    private const string CONTENT_ID = "I_kwDOA";
    private const string ITEM_ID = "PVTI_lADOA";
    private const string CACHED_ITEM_ID = "PVTI_cached";
    private const string FIELD_ID = "PVTSSF_lADOA";
    private const string FIELD_NAME = "Status";
    private const string OPTION_ID = "47fc9ee4";
    private const string ISSUE_URL = "https://github.com/" + REPOSITORY + "/issues/42";
    private const string COULD_NOT_SET = "could not set the " + FIELD_NAME + " of " + ISSUE_URL;
    private const string COULD_NOT_ADD = "could not add " + ISSUE_URL + " to project 7";
    private const string STALE_ERROR =
        """{"errors":[{"message":"Could not resolve to a node with the global id of 'PVTI_cached'"}],"data":null}""";
    private const string OTHER_ERROR =
        """{"errors":[{"message":"Resource not accessible by integration"}],"data":null}""";

    private static readonly WorkflowProject Project = new(ProjectNodeId: PROJECT_ID, Number: 7, Owner: OWNER);

    private readonly RepositoryProjectCache _cache = new(
        new HttpContextAccessor { HttpContext = CallerContext.WithBearerToken() }
    );

    [Fact]
    public async Task CachedItemIsUpdatedWithOneRequestAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static _ => UpdateResponse());

        await this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        RecordedRequest request = Assert.Single(upstream.Requests);
        JsonElement variables = Variables(request);
        Assert.Contains(
            expectedSubstring: "updateProjectV2ItemFieldValue",
            actualString: Query(request),
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: PROJECT_ID, actual: variables.GetProperty("p").GetString());
        Assert.Equal(expected: CACHED_ITEM_ID, actual: variables.GetProperty("i").GetString());
        Assert.Equal(expected: FIELD_ID, actual: variables.GetProperty("f").GetString());
        Assert.Equal(expected: OPTION_ID, actual: variables.GetProperty("o").GetString());
    }

    [Fact]
    public async Task UncachedIssueIsAddedThenUpdatedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => FreshAddResponse(index));

        await this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: 3, actual: upstream.Requests.Count);

        RecordedRequest nodeId = upstream.Requests[0];
        Assert.Contains(
            expectedSubstring: "item:issue(number:$n){id}",
            actualString: Query(nodeId),
            comparisonType: StringComparison.Ordinal
        );
        JsonElement nodeIdVariables = Variables(nodeId);
        Assert.Equal(expected: OWNER, actual: nodeIdVariables.GetProperty("o").GetString());
        Assert.Equal(expected: REPO, actual: nodeIdVariables.GetProperty("r").GetString());
        Assert.Equal(expected: NUMBER, actual: nodeIdVariables.GetProperty("n").GetInt32());

        RecordedRequest add = upstream.Requests[1];
        Assert.Contains(
            expectedSubstring: "addProjectV2ItemById",
            actualString: Query(add),
            comparisonType: StringComparison.Ordinal
        );
        JsonElement addVariables = Variables(add);
        Assert.Equal(expected: PROJECT_ID, actual: addVariables.GetProperty("p").GetString());
        Assert.Equal(expected: CONTENT_ID, actual: addVariables.GetProperty("c").GetString());

        Assert.Equal(expected: ITEM_ID, actual: Variables(upstream.Requests[2]).GetProperty("i").GetString());
    }

    [Fact]
    public async Task UncachedPullRequestUsesThePullRequestQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => FreshAddResponse(index));

        await this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.PULL_REQUEST);

        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Contains(
            expectedSubstring: "item:pullRequest(number:$n){id}",
            actualString: Query(upstream.Requests[0]),
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(
            expected: ITEM_ID,
            actual: this._cache.GetItemId(repository: REPOSITORY, kind: WorkItemKind.PULL_REQUEST, number: NUMBER)
        );
    }

    [Fact]
    public async Task AddedItemIdIsCachedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => FreshAddResponse(index));

        await this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: ITEM_ID, actual: this.CachedItemId());
    }

    [Fact]
    public async Task StaleCachedItemIsAddedAgainAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(STALE_ERROR) : FreshAddResponse(index - 1)
        );

        await this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.Equal(expected: CACHED_ITEM_ID, actual: Variables(upstream.Requests[0]).GetProperty("i").GetString());
        Assert.Equal(expected: ITEM_ID, actual: Variables(upstream.Requests[3]).GetProperty("i").GetString());
        Assert.Equal(expected: ITEM_ID, actual: this.CachedItemId());
    }

    [Fact]
    public async Task FailureAfterStaleRecoveryIsNotRetriedAgainAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                1 => NodeIdResponse(),
                2 => AddResponse(),
                _ => Responses.Json(STALE_ERROR),
            }
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_SET, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task OtherGraphQlErrorOnCachedUpdateIsNotRetriedAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json(OTHER_ERROR));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_SET, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
        Assert.Equal(expected: CACHED_ITEM_ID, actual: this.CachedItemId());
    }

    [Fact]
    public async Task HttpFailureOnCachedUpdateIsNotRetriedAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_SET, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task MissingUpdatedItemOnCachedUpdateCouldNotBeSetAsync()
    {
        this.CacheItem(CACHED_ITEM_ID);
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":null}}}""")
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_SET, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "the updated project item was missing", actual: inner.Message);
    }

    [Fact]
    public async Task MissingUpdatedItemAfterAddCouldNotBeSetAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 2 ? Responses.Json("""{"data":{"updateProjectV2ItemFieldValue":null}}""") : FreshAddResponse(index)
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_SET, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task HttpFailureOfTheNodeIdQueryCouldNotBeAddedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
        Assert.Null(this.CachedItemId());
    }

    [Fact]
    public async Task GraphQlErrorOfTheNodeIdQueryCouldNotBeAddedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json(OTHER_ERROR));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":{"repository":{"item":null}}}""")]
    [InlineData("""{"data":{"repository":{"item":{"id":null}}}}""")]
    public async Task MissingWorkItemIdCouldNotBeAddedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "the work item id was missing", actual: inner.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task FailureOfTheAddCouldNotBeAddedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? NodeIdResponse() : Responses.Json(OTHER_ERROR)
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Null(this.CachedItemId());
    }

    [Fact]
    public async Task MissingAddedItemCouldNotBeAddedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? NodeIdResponse() : Responses.Json("""{"data":{"addProjectV2ItemById":null}}""")
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "the added project item was missing", actual: inner.Message);
    }

    [Theory]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":null}}}""")]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":{"id":null}}}}""")]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":{"id":""}}}}""")]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":{"id":"bad id"}}}}""")]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_lADOA\n"}}}}""")]
    public async Task UnexpectedItemIdIsRejectedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(index => index == 0 ? NodeIdResponse() : Responses.Json(json));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetStatusAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: "unexpected item id returned for " + ISSUE_URL, actual: exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Null(this.CachedItemId());
    }

    [Theory]
    [InlineData("", REPO)]
    [InlineData(OWNER, "")]
    [InlineData("cred feto", REPO)]
    [InlineData(OWNER, "credfeto/extra")]
    public async Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string owner, string repo)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SetStatusAsync(
                    upstream: upstream,
                    owner: owner,
                    repo: repo,
                    number: NUMBER,
                    kind: WorkItemKind.ISSUE,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NumberThatIsNotPositiveIsRejectedBeforeAnyRequestAsync(int number)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SetStatusAsync(
                    upstream: upstream,
                    owner: OWNER,
                    repo: REPO,
                    number: number,
                    kind: WorkItemKind.ISSUE,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => FreshAddResponse(index));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.SetStatusAsync(
                    upstream: upstream,
                    owner: OWNER,
                    repo: REPO,
                    number: NUMBER,
                    kind: WorkItemKind.ISSUE,
                    cancellationToken: cancelled.Token
                )
                .AsTask()
        );
    }

    private static HttpResponseMessage FreshAddResponse(int index)
    {
        return index switch
        {
            0 => NodeIdResponse(),
            1 => AddResponse(),
            _ => UpdateResponse(),
        };
    }

    private static HttpResponseMessage NodeIdResponse()
    {
        return Responses.Json("{\"data\":{\"repository\":{\"item\":{\"id\":\"" + CONTENT_ID + "\"}}}}");
    }

    private static HttpResponseMessage AddResponse()
    {
        return Responses.Json("{\"data\":{\"addProjectV2ItemById\":{\"item\":{\"id\":\"" + ITEM_ID + "\"}}}}");
    }

    private static HttpResponseMessage UpdateResponse()
    {
        return Responses.Json(
            "{\"data\":{\"updateProjectV2ItemFieldValue\":{\"projectV2Item\":{\"id\":\"" + ITEM_ID + "\"}}}}"
        );
    }

    private static string Query(RecordedRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body ?? string.Empty);

        return document.RootElement.GetProperty("query").GetString() ?? string.Empty;
    }

    private static JsonElement Variables(RecordedRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body ?? string.Empty);

        return document.RootElement.GetProperty("variables").Clone();
    }

    private void CacheItem(string itemId)
    {
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: NUMBER, itemId: itemId);
    }

    private string? CachedItemId()
    {
        return this._cache.GetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: NUMBER);
    }

    private BoardWriter CreateWriter(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client, cache: this._cache, logger: this.GetTypedLogger<BoardWriter>());
    }

    private ValueTask SetStatusAsync(HttpMessageHandler upstream, WorkItemKind kind)
    {
        return this.SetStatusAsync(
            upstream: upstream,
            owner: OWNER,
            repo: REPO,
            number: NUMBER,
            kind: kind,
            cancellationToken: this.CancellationToken()
        );
    }

    private ValueTask SetStatusAsync(
        HttpMessageHandler upstream,
        string owner,
        string repo,
        int number,
        WorkItemKind kind,
        CancellationToken cancellationToken
    )
    {
        return this.CreateWriter(upstream)
            .SetItemStatusAsync(
                owner: owner,
                repo: repo,
                kind: kind,
                number: number,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: cancellationToken
            );
    }
}
