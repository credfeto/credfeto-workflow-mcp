using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class BoardFallbackWriterTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string PROJECT_ID = "PVT_kwDOA";
    private const string FIELD_ID = "PVTSSF_status";
    private const string NOT_STARTED_OPTION_ID = "opt_not_started";
    private const string CONTENT_ID = "I_kwDOABCDEF";
    private const string ITEM_ID = "PVTI_lADOA";
    private const string ISSUE_URL = "https://github.com/" + REPOSITORY + "/issues/42";
    private const string COULD_NOT_ADD =
        "could not add " + ISSUE_URL + " to the Workflow board as Not Started with the board token";

    private const string PROJECTS =
        """{"data":{"repository":{"projectsV2":{"nodes":[{"id":"PVT_kwDOA","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}""";

    private const string STATUS_FIELDS =
        """{"data":{"node":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","options":[{"id":"opt_dev","name":"Development"},{"id":"opt_not_started","name":"Not Started"},{"id":"opt_review","name":"AI Review"},{"id":"opt_complete","name":"Complete"}]}]}}}}""";

    private const string STATUS_FIELDS_WITHOUT_NOT_STARTED =
        """{"data":{"node":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","options":[{"id":"opt_dev","name":"Development"},{"id":"opt_complete","name":"Complete"}]}]}}}}""";

    private const string ADDED = """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_lADOA"}}}}""";

    private const string UPDATED =
        """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_lADOA"}}}}""";

    private const string FORBIDDEN =
        """{"errors":[{"type":"FORBIDDEN","message":"Resource not accessible by integration"}],"data":null}""";

    private static readonly Uri IssueUrl = new(ISSUE_URL);

    [Fact]
    public async Task AddsTheItemAndSetsItToNotStartedOnlyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));

        await this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID);

        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        AssertQueryContains(request: upstream.Requests[0], expected: "projectsV2");
        AssertQueryContains(request: upstream.Requests[1], expected: "fields");
        AssertQueryContains(request: upstream.Requests[2], expected: "addProjectV2ItemById");
        AssertQueryContains(request: upstream.Requests[3], expected: "updateProjectV2ItemFieldValue");

        JsonElement add = Variables(upstream.Requests[2]);
        Assert.Equal(expected: PROJECT_ID, actual: add.GetProperty("p").GetString());
        Assert.Equal(expected: CONTENT_ID, actual: add.GetProperty("c").GetString());

        JsonElement update = Variables(upstream.Requests[3]);
        Assert.Equal(expected: PROJECT_ID, actual: update.GetProperty("p").GetString());
        Assert.Equal(expected: ITEM_ID, actual: update.GetProperty("i").GetString());
        Assert.Equal(expected: FIELD_ID, actual: update.GetProperty("f").GetString());
        Assert.Equal(expected: NOT_STARTED_OPTION_ID, actual: update.GetProperty("o").GetString());
    }

    [Fact]
    public async Task CheckOnlyReadsTheProjectAndItsStatusFieldAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));

        await this.CreateWriter(upstream)
            .CheckNotStartedAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertQueryContains(request: upstream.Requests[0], expected: "projectsV2");
        AssertQueryContains(request: upstream.Requests[1], expected: "fields");
        Assert.All(collection: upstream.Requests, action: static request => AssertIsNotAMutation(request));
    }

    [Fact]
    public async Task CheckFailsWhenTheBoardHasNoNotStartedStatusAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(PROJECTS) : Responses.Json(STATUS_FIELDS_WITHOUT_NOT_STARTED)
        );

        _ = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.CreateWriter(upstream)
                .CheckNotStartedAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CheckFailsWhenTheBoardCannotBeFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"repository":{"projectsV2":{"nodes":[],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}"""
            )
        );

        _ = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CreateWriter(upstream)
                .CheckNotStartedAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task NothingIsAddedWhenTheBoardHasNoNotStartedStatusAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(PROJECTS) : Responses.Json(STATUS_FIELDS_WITHOUT_NOT_STARTED)
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID)
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        _ = Assert.IsType<StatusOptionMissingException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.All(collection: upstream.Requests, action: static request => AssertIsNotAMutation(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData("I_kwDOA\n")]
    [InlineData("I_kwDOA\"}")]
    public async Task UnexpectedNodeIdIsRefusedBeforeAnyRequestAsync(string contentNodeId)
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: contentNodeId)
        );

        Assert.Equal(expected: "unexpected node id for " + ISSUE_URL, actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task RefusedAddIsWrappedAndNotUpdatedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 2 ? Responses.Json(FORBIDDEN) : Success(index)
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID)
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        GitHubGraphQlException inner = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        Assert.Equal(expected: ["FORBIDDEN"], actual: inner.ErrorTypes);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task HttpFailureIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID)
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        WorkflowBoardInvalidException inner = Assert.IsType<WorkflowBoardInvalidException>(exception.InnerException);
        _ = Assert.IsType<HttpRequestException>(inner.InnerException);
    }

    [Theory]
    [InlineData("""{"data":{"addProjectV2ItemById":null}}""")]
    [InlineData("""{"data":{"addProjectV2ItemById":{"item":{"id":"bad id"}}}}""")]
    public async Task MissingAddedItemIsWrappedAndNotUpdatedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(index => index == 2 ? Responses.Json(json) : Success(index));

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID)
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "the added project item was missing", actual: inner.Message);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MissingUpdatedItemIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 3 ? Responses.Json("""{"data":{"updateProjectV2ItemFieldValue":null}}""") : Success(index)
        );

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.AddAsync(upstream: upstream, contentNodeId: CONTENT_ID)
        );

        Assert.Equal(expected: COULD_NOT_ADD, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "the updated project item was missing", actual: inner.Message);
    }

    [Fact]
    public async Task MalformedRepositoryIsRefusedBeforeAnyRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateWriter(upstream)
                .CheckNotStartedAsync(repository: "credfeto", cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("github_pat_board", true)]
    public void IsConfiguredFollowsTheBoardToken(string? token, bool expected)
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));

        BoardFallbackWriter writer = this.CreateWriter(upstream: upstream, token: token);

        Assert.Equal(expected: expected, actual: writer.IsConfigured);
    }

    private static HttpResponseMessage Success(int index)
    {
        return index switch
        {
            0 => Responses.Json(PROJECTS),
            1 => Responses.Json(STATUS_FIELDS),
            2 => Responses.Json(ADDED),
            _ => Responses.Json(UPDATED),
        };
    }

    private static void AssertQueryContains(RecordedRequest request, string expected)
    {
        Assert.Contains(
            expectedSubstring: expected,
            actualString: Query(request),
            comparisonType: StringComparison.Ordinal
        );
    }

    private static void AssertIsNotAMutation(RecordedRequest request)
    {
        Assert.DoesNotContain(
            expectedSubstring: "mutation",
            actualString: Query(request),
            comparisonType: StringComparison.Ordinal
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

    private Task AddAsync(HttpMessageHandler upstream, string contentNodeId)
    {
        return this.CreateWriter(upstream)
            .AddAsNotStartedAsync(
                repository: REPOSITORY,
                contentNodeId: contentNodeId,
                url: IssueUrl,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    private BoardFallbackWriter CreateWriter(HttpMessageHandler upstream, string? token = "github_pat_board")
    {
        IOptions<GitHubOptions> options = Options.Create(
            new GitHubOptions { Board = new GitHubBoardOptions { Token = token } }
        );

        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(
                upstream: upstream,
                httpClientName: GitHubSetup.BoardHttpClientName
            ),
            options: options,
            httpClientName: GitHubSetup.BoardHttpClientName,
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client, options: options, logger: this.GetTypedLogger<BoardFallbackWriter>());
    }
}
