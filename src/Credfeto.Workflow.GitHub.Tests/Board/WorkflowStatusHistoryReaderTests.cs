using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class WorkflowStatusHistoryReaderTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string REPO = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + REPO;
    private const int NUMBER = 7;
    private const string PROJECT_ID = "PVT_kwDOA";
    private const string OTHER_PROJECT_ID = "PVT_other";
    private const string TIME = "2026-01-01T00:00:00Z";

    private static readonly WorkflowProject Project = new(ProjectNodeId: PROJECT_ID, Number: 1, Owner: OWNER);

    private readonly IWorkflowBoardResolver _boardResolver;

    public WorkflowStatusHistoryReaderTests()
    {
        this._boardResolver = GetSubstitute<IWorkflowBoardResolver>();
        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);
    }

    [Fact]
    public async Task ReadsTheChangesOnTheBoardOldestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                nodes:
                [
                    Event(previous: "", status: "Development", actor: "credfeto", time: "2026-01-01T00:00:00Z"),
                    Event(
                        previous: "Development",
                        status: "AI Review",
                        actor: "steward[bot]",
                        time: "2026-01-02T00:00:00Z"
                    ),
                ]
            )
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(expected: NUMBER, actual: result.Number);
        Assert.Equal(
            expected:
            [
                new WorkflowStatusHistoryEntry(
                    Time: "2026-01-01T00:00:00Z",
                    Actor: "credfeto",
                    PreviousStatus: "-",
                    Status: "Development"
                ),
                new WorkflowStatusHistoryEntry(
                    Time: "2026-01-02T00:00:00Z",
                    Actor: "steward[bot]",
                    PreviousStatus: "Development",
                    Status: "AI Review"
                ),
            ],
            actual: result.Changes
        );
    }

    [Fact]
    public async Task AnIssueIsReadWithTheIssueQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: [Event()]));

        _ = await this.ReadAsync(upstream);

        using JsonDocument body = SingleBody(upstream);
        AssertQuery(body: body, expected: "item:issue(number:$n)", cursor: null);
    }

    [Fact]
    public async Task APullRequestIsReadWithThePullRequestQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: [Event()]));

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream: upstream, issue: null, pr: NUMBER);

        _ = Assert.Single(result.Changes);
        using JsonDocument body = SingleBody(upstream);
        AssertQuery(body: body, expected: "item:pullRequest(number:$n)", cursor: null);
    }

    [Fact]
    public async Task OnlyChangesOnTheWorkflowBoardAreReturnedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                nodes:
                [
                    Event(status: "other", projectId: OTHER_PROJECT_ID),
                    Event(status: "kept"),
                    Event(status: "no project", projectId: null),
                    "{}",
                    "null",
                ]
            )
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        WorkflowStatusHistoryEntry entry = Assert.Single(result.Changes);
        Assert.Equal(expected: "kept", actual: entry.Status);
    }

    [Fact]
    public async Task NoChangesIsAnEmptyListAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: []));

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task MissingNodesIsAnEmptyListAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(
                "{\"data\":{\"repository\":{\"item\":{\"timelineItems\":{\"nodes\":null,\"pageInfo\":"
                    + PageInfo(hasNextPage: false, endCursor: null)
                    + "}}}}}"
            )
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task MissingTextIsShownAsADashAndLineBreaksAreReplacedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                nodes:
                [
                    Event(previous: null, status: "a\\u0001b", actor: null),
                    Event(previous: "", status: "", actor: ""),
                ]
            )
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: ["-", "-"],
            actual: result.Changes.Select(static c => c.Actor),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(
            expected: ["-", "-"],
            actual: result.Changes.Select(static c => c.PreviousStatus),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(
            expected: ["a b", "-"],
            actual: result.Changes.Select(static c => c.Status),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Page(nodes: [Event(status: "first")], hasNextPage: true, endCursor: "cursor-1"),
                1 => Page(nodes: [Event(status: "second")]),
                _ => throw new InvalidOperationException("Unexpected request"),
            }
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: ["first", "second"],
            actual: result.Changes.Select(static c => c.Status),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument second = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        AssertQuery(body: second, expected: "item:issue(number:$n)", cursor: "cursor-1");
    }

    [Fact]
    public async Task ExactlyTheCapIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index < 9 ? Page(nodes: Events(100), hasNextPage: true, endCursor: Cursor(index)) : Page(nodes: Events(100))
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(expected: 1000, actual: result.Changes.Count);
    }

    [Fact]
    public async Task OverTheCapIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Page(nodes: Events(100), hasNextPage: true, endCursor: Cursor(index))
        );

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#7 has more than 1000 Workflow status changes to read",
            actual: exception.Message
        );
        Assert.Equal(expected: 11, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task EmptyPagesThatNeverEndAreIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Page(nodes: [], hasNextPage: true, endCursor: Cursor(index))
        );

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 11, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMissingCursorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(nodes: [Event()], hasNextPage: true, endCursor: null)
        );

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ARepeatedCursorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(nodes: [Event()], hasNextPage: true, endCursor: "same")
        );

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            NotFound("Could not resolve to an Issue with the number of 7.")
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "the issue credfeto/credfeto-workflow-mcp#7 was not found, or the caller cannot see it",
            actual: exception.Message
        );
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            NotFound("Could not resolve to a PullRequest with the number of 7.")
        );

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream: upstream, issue: null, pr: NUMBER)
        );

        Assert.Equal(
            expected: "the pull request credfeto/credfeto-workflow-mcp#7 was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task ANullItemIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json("{\"data\":{\"repository\":{\"item\":null}}}")
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ANullRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json("{\"data\":{\"repository\":null}}"));

        _ = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream: upstream, issue: null, pr: NUMBER)
        );
    }

    [Fact]
    public async Task AnotherGraphQlErrorIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json("""{"errors":[{"message":"Something went wrong"}],"data":null}""")
        );

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read the Workflow status history of credfeto/credfeto-workflow-mcp#7",
            actual: exception.Message
        );
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task AnUnreadableResponseIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("{\"data\":{\"repository\":{\"item\":{\"timelineItems\":null}}}}")]
    [InlineData("{\"data\":{\"repository\":{\"item\":{\"timelineItems\":{\"nodes\":[],\"pageInfo\":null}}}}}")]
    public async Task AnIncompleteResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("yesterday")]
    [InlineData("2026-01-02T03:04:05+01:00")]
    public async Task AChangeWithoutAValidTimeIsFailedAsync(string? time)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: [Event(time: time)]));

        WorkflowStatusHistoryFailedException exception = await Assert.ThrowsAsync<WorkflowStatusHistoryFailedException>(
            () =>
                this.ReadAsync(upstream)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task ABadTimeOnAnotherBoardIsIgnoredAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(nodes: [Event(time: "yesterday", projectId: OTHER_PROJECT_ID)])
        );

        WorkflowStatusHistoryResult result = await this.ReadAsync(upstream);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task ABoardThatCannotBeResolvedPropagatesWithoutARequestAsync()
    {
        WorkflowBoardNotFoundException failure = new("no board");
        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = new(static _ => throw new InvalidOperationException("no request"));

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("", 7, null, "repository")]
    [InlineData("credfeto", 7, null, "repository")]
    [InlineData(REPOSITORY, null, null, "pr")]
    [InlineData(REPOSITORY, 7, 8, "pr")]
    [InlineData(REPOSITORY, 0, null, "issue")]
    [InlineData(REPOSITORY, -1, null, "issue")]
    [InlineData(REPOSITORY, null, 0, "pr")]
    [InlineData(REPOSITORY, null, -1, "pr")]
    public async Task InvalidInputIsRejectedWithoutRequestAsync(
        string repository,
        int? issue,
        int? pr,
        string parameter
    )
    {
        using RecordingHttpMessageHandler upstream = new(static _ => throw new InvalidOperationException("no request"));

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, repository: repository, issue: issue, pr: pr)
        );

        Assert.Contains(
            expectedSubstring: parameter,
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
        await this
            ._boardResolver.DidNotReceive()
            .ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    private async Task<WorkflowStatusHistoryResult> ReadAsync(
        HttpMessageHandler upstream,
        string repository = REPOSITORY,
        int? issue = NUMBER,
        int? pr = null
    )
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return await new WorkflowStatusHistoryReader(
            graphQlClient: client,
            boardResolver: this._boardResolver
        ).ReadAsync(repository: repository, issue: issue, pr: pr, cancellationToken: this.CancellationToken());
    }

    private static JsonDocument SingleBody(RecordingHttpMessageHandler upstream)
    {
        RecordedRequest request = Assert.Single(upstream.Requests);

        return JsonDocument.Parse(request.Body ?? string.Empty);
    }

    private static void AssertQuery(JsonDocument body, string expected, string? cursor)
    {
        JsonElement root = body.RootElement;
        Assert.Contains(
            expectedSubstring: expected,
            actualString: root.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "itemTypes:[PROJECT_V2_ITEM_STATUS_CHANGED_EVENT]",
            actualString: root.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );

        JsonElement variables = root.GetProperty("variables");
        Assert.Equal(expected: OWNER, actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: REPO, actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: NUMBER, actual: variables.GetProperty("n").GetInt32());

        string? actualCursor = variables.TryGetProperty(propertyName: "c", value: out JsonElement c)
            ? c.GetString()
            : null;
        Assert.Equal(expected: cursor, actual: actualCursor);
    }

    private static HttpResponseMessage NotFound(string error)
    {
        return Responses.Json(
            "{\"errors\":[{\"message\":\"" + error + "\"}],\"data\":{\"repository\":{\"item\":null}}}"
        );
    }

    private static HttpResponseMessage Page(string[] nodes, bool hasNextPage = false, string? endCursor = null)
    {
        return Responses.Json(
            "{\"data\":{\"repository\":{\"item\":{\"timelineItems\":{\"nodes\":["
                + string.Join(separator: ',', value: nodes)
                + "],\"pageInfo\":"
                + PageInfo(hasNextPage: hasNextPage, endCursor: endCursor)
                + "}}}}}"
        );
    }

    private static string PageInfo(bool hasNextPage, string? endCursor)
    {
        return "{\"hasNextPage\":" + (hasNextPage ? "true" : "false") + ",\"endCursor\":" + Json(endCursor) + "}";
    }

    private static string Cursor(int index)
    {
        return "cursor-" + index.ToString(CultureInfo.InvariantCulture);
    }

    private static string[] Events(int count)
    {
        return [.. Enumerable.Range(0, count).Select(static _ => Event())];
    }

    private static string Event(
        string? previous = "Development",
        string? status = "AI Review",
        string? actor = "credfeto",
        string? time = TIME,
        string? projectId = PROJECT_ID
    )
    {
        string project = projectId is null ? "null" : "{\"id\":" + Json(projectId) + "}";
        string user = actor is null ? "null" : "{\"login\":" + Json(actor) + "}";

        return "{\"createdAt\":"
            + Json(time)
            + ",\"actor\":"
            + user
            + ",\"previousStatus\":"
            + Json(previous)
            + ",\"status\":"
            + Json(status)
            + ",\"project\":"
            + project
            + "}";
    }

    // Values are written raw so tests can pass JSON escapes such as \u0001 through to the reader
    private static string Json(string? value)
    {
        return value is null ? "null" : "\"" + value + "\"";
    }
}
