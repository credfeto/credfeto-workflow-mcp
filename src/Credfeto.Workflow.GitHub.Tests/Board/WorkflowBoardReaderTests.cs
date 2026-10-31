using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class WorkflowBoardReaderTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string REPO = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + REPO;
    private const int NUMBER = 42;
    private const string PROJECT_ID = "PVT_kwDOA";
    private const string OTHER_PROJECT_ID = "PVT_other";
    private const string NO_VALUE = "null";
    private const string COULD_NOT_READ =
        "could not read the status of " + REPOSITORY + "#42: both the direct query and the listing of project 7 failed";

    private static readonly WorkflowProject Project = new(ProjectNodeId: PROJECT_ID, Number: 7, Owner: OWNER);

    private static readonly WorkflowStatusField Field = new(
        FieldId: "PVTSSF_field",
        FieldName: "Status",
        OptionIds: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Development"] = "option-development",
            ["Human Review"] = "option-human-review",
        }
    );

    private static readonly BoardItemStatus NotOnBoard = new(State: BoardItemState.NOT_ON_BOARD, Name: null);
    private static readonly BoardItemStatus Unset = new(State: BoardItemState.UNSET, Name: null);

    [Theory]
    [InlineData("\"AI Review\"", "\"Development\"", "AI Review")]
    [InlineData("\"AI Review\"", NO_VALUE, "AI Review")]
    [InlineData(NO_VALUE, "\"Development\"", "Development")]
    [InlineData("\"\"", "\"Human Review\"", "Human Review")]
    public async Task DirectReadReturnsTheStateAsync(string legacy, string status, string expected)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            DirectPage(nodes: DirectNode(projectId: PROJECT_ID, legacy: legacy, status: status))
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: new BoardItemStatus(State: BoardItemState.STATE, Name: expected), actual: result);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(NO_VALUE, NO_VALUE)]
    [InlineData("\"\"", NO_VALUE)]
    [InlineData(NO_VALUE, "\"Todo\"")]
    [InlineData("\"\"", "\"development\"")]
    [InlineData("{}", "{}")]
    public async Task DirectReadWithoutAWorkflowStateIsUnsetAsync(string legacy, string status)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            DirectPage(nodes: DirectNode(projectId: PROJECT_ID, legacy: legacy, status: status))
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: Unset, actual: result);
    }

    [Fact]
    public async Task DirectReadIgnoresOtherProjectsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            DirectPage(
                nodes: "null,"
                    + DirectNode(projectId: OTHER_PROJECT_ID, legacy: "\"Complete\"", status: NO_VALUE)
                    + ","
                    + """{"project":null,"legacy":null,"status":null}"""
                    + ","
                    + DirectNode(projectId: PROJECT_ID, legacy: "\"Development\"", status: NO_VALUE)
            )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: new BoardItemStatus(State: BoardItemState.STATE, Name: "Development"), actual: result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task DirectReadOfACompleteListWithoutTheBoardIsNotOnBoardAsync(string nodes)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            DirectPage(
                nodes: nodes.Length == 0
                    ? DirectNode(projectId: OTHER_PROJECT_ID, legacy: "\"Development\"", status: NO_VALUE)
                    : nodes
            )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: NotOnBoard, actual: result);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task DirectReadWithNullNodesIsNotOnBoardAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"repository":{"item":{"projectItems":{"nodes":null,"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}}"""
            )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: NotOnBoard, actual: result);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("Could not resolve to an Issue with the number of 42.")]
    [InlineData("Could not resolve to a PullRequest with the number of 42.")]
    public async Task WorkItemThatDoesNotExistIsNotOnBoardAsync(string error)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json("{\"errors\":[{\"message\":\"" + error + "\"}],\"data\":{\"repository\":{\"item\":null}}}")
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: NotOnBoard, actual: result);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(WorkItemKind.ISSUE, "item:issue(number:$n)")]
    [InlineData(WorkItemKind.PULL_REQUEST, "item:pullRequest(number:$n)")]
    public async Task DirectQueryFollowsTheKindOfWorkItemAsync(WorkItemKind kind, string accessor)
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            DirectPage(nodes: DirectNode(projectId: PROJECT_ID, legacy: "\"Development\"", status: NO_VALUE))
        );

        _ = await this.ReadAsync(upstream: upstream, kind: kind);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        Assert.Contains(
            expectedSubstring: accessor,
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );

        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: OWNER, actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: REPO, actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: NUMBER, actual: variables.GetProperty("n").GetInt32());
    }

    public static TheoryData<string> DirectReadFailures()
    {
        return
        [
            "status",
            "html",
            """{"errors":[{"message":"Something went wrong"}],"data":null}""",
            """{"data":null}""",
            """{"data":{"repository":null}}""",
            """{"data":{"repository":{"item":null}}}""",
            """{"data":{"repository":{"item":{"projectItems":null}}}}""",
            """{"data":{"repository":{"item":{"projectItems":{"nodes":[],"pageInfo":null}}}}}""",
            """{"data":{"repository":{"item":{"projectItems":{"nodes":[{"project":{"id":"PVT_other"},"legacy":null,"status":null}],"pageInfo":{"hasNextPage":true,"endCursor":"cursor-1"}}}}}}""",
        ];
    }

    [Theory]
    [MemberData(nameof(DirectReadFailures))]
    public async Task FailedDirectReadFallsBackToTheListingAsync(string direct)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? DirectResponse(direct)
                : ListingPage(
                    nodes: ListingNode(
                        type: "ISSUE",
                        number: NUMBER,
                        repository: REPOSITORY,
                        legacy: NO_VALUE,
                        status: "\"Development\""
                    )
                )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: new BoardItemStatus(State: BoardItemState.STATE, Name: "Development"), actual: result);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument listing = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        Assert.Contains(
            expectedSubstring: "items(first:100,after:$c)",
            actualString: listing.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );

        JsonElement variables = listing.RootElement.GetProperty("variables");
        Assert.Equal(expected: PROJECT_ID, actual: variables.GetProperty("project").GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("c").ValueKind);
    }

    [Theory]
    [InlineData(WorkItemKind.ISSUE, "ISSUE", REPOSITORY)]
    [InlineData(WorkItemKind.ISSUE, "ISSUE", "Credfeto/Credfeto-Workflow-MCP")]
    [InlineData(WorkItemKind.PULL_REQUEST, "PULL_REQUEST", REPOSITORY)]
    public async Task ListingFindsTheWorkItemAsync(WorkItemKind kind, string type, string repository)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : ListingPage(
                    nodes: ListingNode(
                        type: type,
                        number: NUMBER,
                        repository: repository,
                        legacy: "\"Human Review\"",
                        status: NO_VALUE
                    )
                )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: kind);

        Assert.Equal(expected: new BoardItemStatus(State: BoardItemState.STATE, Name: "Human Review"), actual: result);
    }

    [Fact]
    public async Task ListingWithoutAWorkflowStateIsUnsetAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : ListingPage(
                    nodes: ListingNode(
                        type: "ISSUE",
                        number: NUMBER,
                        repository: REPOSITORY,
                        legacy: NO_VALUE,
                        status: "\"Todo\""
                    )
                )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: Unset, actual: result);
    }

    [Fact]
    public async Task ListingIgnoresOtherWorkItemsAsync()
    {
        string others =
            "null,"
            + ListingNode(
                type: "PULL_REQUEST",
                number: NUMBER,
                repository: REPOSITORY,
                legacy: "\"Complete\"",
                status: NO_VALUE
            )
            + ","
            + ListingNode(
                type: "ISSUE",
                number: NUMBER + 1,
                repository: REPOSITORY,
                legacy: "\"Complete\"",
                status: NO_VALUE
            )
            + ","
            + ListingNode(
                type: "ISSUE",
                number: NUMBER,
                repository: "credfeto/other",
                legacy: "\"Complete\"",
                status: NO_VALUE
            )
            + ","
            + """{"type":"ISSUE","content":{"number":42,"repository":null},"legacy":{"name":"Complete"},"status":null}"""
            + ","
            + """{"type":"DRAFT_ISSUE","content":null,"legacy":{"name":"Complete"},"status":null}"""
            + ","
            + """{"type":"ISSUE","content":null,"legacy":{"name":"Complete"},"status":null}"""
            + ","
            + """{"type":null,"content":{},"legacy":null,"status":null}""";

        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(HttpStatusCode.BadGateway) : ListingPage(nodes: others)
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: NotOnBoard, actual: result);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ListingWithNullNodesIsNotOnBoardAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : Responses.Json(
                    """{"data":{"node":{"items":{"nodes":null,"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}"""
                )
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: NotOnBoard, actual: result);
    }

    [Fact]
    public async Task ListingFindsTheWorkItemOnALaterPageUsingTheCursorAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Status(HttpStatusCode.BadGateway),
                1 => ListingPage(nodes: string.Empty, hasNextPage: true, endCursor: "cursor-1"),
                _ => ListingPage(
                    nodes: ListingNode(
                        type: "ISSUE",
                        number: NUMBER,
                        repository: REPOSITORY,
                        legacy: "\"Development\"",
                        status: NO_VALUE
                    )
                ),
            }
        );

        BoardItemStatus result = await this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE);

        Assert.Equal(expected: new BoardItemStatus(State: BoardItemState.STATE, Name: "Development"), actual: result);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);

        using JsonDocument second = JsonDocument.Parse(upstream.Requests[2].Body ?? string.Empty);
        Assert.Equal(
            expected: "cursor-1",
            actual: second.RootElement.GetProperty("variables").GetProperty("c").GetString()
        );
    }

    [Theory]
    [InlineData("""{"data":{"node":null}}""")]
    [InlineData("""{"data":{"node":{}}}""")]
    [InlineData("""{"data":{"node":{"items":null}}}""")]
    [InlineData("""{"data":{"node":{"items":{"nodes":[],"pageInfo":null}}}}""")]
    [InlineData("""{"data":{"node":{"items":{"nodes":[]}}}}""")]
    [InlineData("""{"data":{"node":{"items":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":null}}}}}""")]
    public async Task IncompleteListingCouldNotBeReadAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(HttpStatusCode.BadGateway) : Responses.Json(json)
        );

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task RepeatedListingCursorCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : ListingPage(nodes: string.Empty, hasNextPage: true, endCursor: "same-cursor")
        );

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task EndlessListingIsCappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : ListingPage(
                    nodes: string.Empty,
                    hasNextPage: true,
                    endCursor: "cursor-" + index.ToString(CultureInfo.InvariantCulture)
                )
        );

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Equal(expected: 101, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task HttpFailureOfBothReadsCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task GraphQlErrorsOfTheListingCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Responses.Status(HttpStatusCode.BadGateway)
                : Responses.Json("""{"errors":[{"message":"Could not resolve to a node"}],"data":null}""")
        );

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableListingCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Status(HttpStatusCode.BadGateway) : Responses.Html("<html></html>")
        );

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.ReadAsync(upstream: upstream, kind: WorkItemKind.ISSUE).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
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
            this.CreateReader(upstream)
                .ReadStatusAsync(
                    owner: owner,
                    repo: repo,
                    kind: WorkItemKind.ISSUE,
                    number: NUMBER,
                    project: Project,
                    field: Field,
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
            this.CreateReader(upstream)
                .ReadStatusAsync(
                    owner: OWNER,
                    repo: REPO,
                    kind: WorkItemKind.ISSUE,
                    number: number,
                    project: Project,
                    field: Field,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            DirectPage(nodes: DirectNode(projectId: PROJECT_ID, legacy: "\"Development\"", status: NO_VALUE))
        );
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateReader(upstream)
                .ReadStatusAsync(
                    owner: OWNER,
                    repo: REPO,
                    kind: WorkItemKind.ISSUE,
                    number: NUMBER,
                    project: Project,
                    field: Field,
                    cancellationToken: cancelled.Token
                )
                .AsTask()
        );
    }

    private static HttpResponseMessage DirectResponse(string direct)
    {
        return direct switch
        {
            "status" => Responses.Status(HttpStatusCode.BadGateway),
            "html" => Responses.Html("<html></html>"),
            _ => Responses.Json(direct),
        };
    }

    private static string DirectNode(string projectId, string legacy, string status)
    {
        return "{\"project\":{\"id\":\""
            + projectId
            + "\"},\"legacy\":"
            + FieldValue(legacy)
            + ",\"status\":"
            + FieldValue(status)
            + "}";
    }

    private static string ListingNode(string type, int number, string repository, string legacy, string status)
    {
        return "{\"type\":\""
            + type
            + "\",\"content\":{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"repository\":{\"nameWithOwner\":\""
            + repository
            + "\"}},\"legacy\":"
            + FieldValue(legacy)
            + ",\"status\":"
            + FieldValue(status)
            + "}";
    }

    // A field value is either null, an object as-is, or a quoted option name wrapped as {"name":...}
    private static string FieldValue(string value)
    {
        return value[0] == '"' ? "{\"name\":" + value + "}" : value;
    }

    private static HttpResponseMessage DirectPage(string nodes, bool hasNextPage = false, string? endCursor = null)
    {
        return Responses.Json(
            "{\"data\":{\"repository\":{\"item\":{\"projectItems\":{\"nodes\":["
                + nodes
                + "],\"pageInfo\":"
                + PageInfo(hasNextPage: hasNextPage, endCursor: endCursor)
                + "}}}}}"
        );
    }

    private static HttpResponseMessage ListingPage(string nodes, bool hasNextPage = false, string? endCursor = null)
    {
        return Responses.Json(
            "{\"data\":{\"node\":{\"items\":{\"nodes\":["
                + nodes
                + "],\"pageInfo\":"
                + PageInfo(hasNextPage: hasNextPage, endCursor: endCursor)
                + "}}}}"
        );
    }

    private static string PageInfo(bool hasNextPage, string? endCursor)
    {
        string cursor = endCursor is null ? "null" : "\"" + endCursor + "\"";

        return "{\"hasNextPage\":" + (hasNextPage ? "true" : "false") + ",\"endCursor\":" + cursor + "}";
    }

    private WorkflowBoardReader CreateReader(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client, logger: this.GetTypedLogger<WorkflowBoardReader>());
    }

    private ValueTask<BoardItemStatus> ReadAsync(HttpMessageHandler upstream, WorkItemKind kind)
    {
        return this.CreateReader(upstream)
            .ReadStatusAsync(
                owner: OWNER,
                repo: REPO,
                kind: kind,
                number: NUMBER,
                project: Project,
                field: Field,
                cancellationToken: this.CancellationToken()
            );
    }
}
