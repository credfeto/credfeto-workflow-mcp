using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrCreateServiceTests : PrCreateServiceTestBase
{
    private const string EXISTING_NOTE =
        "an open pull request already exists for the --head (base main); its title and body were not changed";

    [Fact]
    public async Task ANewPullRequestIsCreatedAsADraftAndPutOnTheBoardAsync()
    {
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new Uri(PR_URL), actual: response.Url);
        Assert.True(condition: response.Created, userMessage: "The pull request should be created");
        Assert.Null(response.Note);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: BranchUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: PullsUri, actual: upstream.Requests[1].RequestUri);
        Assert.Equal(
            expected: "{\"title\":\"feat(mcp): add pr create\",\"head\":\"feat/28-pr-create\",\"base\":\"main\",\"body\":\"## Summary\\n\\nCreate a pull request.\",\"draft\":true}",
            actual: upstream.Requests[1].Body
        );
        await this.ReceivedBoardWriteAsync();
    }

    [Fact]
    public async Task TheDevelopmentStatusIsResolvedOnTheRepositoryBoardAsync()
    {
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        await this.CreateAsync(upstream: upstream);

        await this
            .StatusResolver.Received(1)
            .ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: WorkflowStates.DEVELOPMENT,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AMissingBaseUsesTheDefaultBranchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("{\"default_branch\":\"main\"}"),
                1 => BranchFound(),
                2 => CreatedPullRequest(PR_URL),
                _ => Unexpected(index),
            }
        );

        PrCreateResponse response = await this.CreateAsync(upstream: upstream, baseBranch: null);

        Assert.True(condition: response.Created, userMessage: "The pull request should be created");
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: RepositoryUri, actual: upstream.Requests[0].RequestUri);
        Assert.Contains(
            expectedSubstring: "\"base\":\"main\"",
            actualString: upstream.Requests[2].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task AMissingRepositoryIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.CreateAsync(upstream: upstream, baseBranch: null)
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{}")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "{\"default_branch\":\"\"}")]
    [InlineData(HttpStatusCode.OK, "{\"default_branch\":\"a..b\"}")]
    public async Task AnUnreadableDefaultBranchIsReportedAsync(HttpStatusCode statusCode, string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithJson(statusCode: statusCode, json: json)
        );

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream, baseBranch: null)
        );

        Assert.Equal(expected: "could not read the default branch of " + REPOSITORY, actual: exception.Message);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AHeadThatIsTheDefaultBranchIsRejectedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json("{\"default_branch\":\"" + HEAD + "\"}") : Unexpected(index)
        );

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream, baseBranch: null)
        );

        Assert.Equal(expected: "the --head is the default branch of " + REPOSITORY, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AMissingHeadIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "the --head was not found in " + REPOSITORY + ": push the branch first",
            actual: exception.Message
        );
        Assert.All(
            collection: upstream.Requests,
            action: request => Assert.Equal(expected: HttpMethod.Get, actual: request.Method)
        );
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AnUnreadableHeadIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.BadGateway));

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "could not read the --head in " + REPOSITORY, actual: exception.Message);
        Assert.All(
            collection: upstream.Requests,
            action: request => Assert.Equal(expected: HttpMethod.Get, actual: request.Method)
        );
    }

    [Fact]
    public async Task AHeadWithADifferentNameIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json("{\"name\":\"other\"}") : Unexpected(index)
        );

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "unexpected result for the --head in " + REPOSITORY, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AnExistingPullRequestIsReturnedWithoutAnyRequestAsync()
    {
        this.OpenPullRequests(Entry());
        this.BoardStatus(state: BoardItemState.STATE, name: WorkflowStates.AI_REVIEW);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new Uri(PR_URL), actual: response.Url);
        Assert.False(condition: response.Created, userMessage: "The existing pull request should be returned");
        Assert.Equal(expected: EXISTING_NOTE, actual: response.Note);
        Assert.Empty(upstream.Requests);
        await this
            .PullRequestList.Received(1)
            .ListAsync(
                repository: REPOSITORY,
                issue: null,
                head: HEAD,
                labels: null,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task APullRequestFromAForkIsNotAMatchAsync()
    {
        this.OpenPullRequests(
            Entry(url: "https://github.com/credfeto/credfeto-workflow-mcp/pull/94", number: 94, flags: ["fork"]),
            Entry(head: "other", url: "https://github.com/credfeto/credfeto-workflow-mcp/pull/95", number: 95)
        );
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.True(condition: response.Created, userMessage: "The pull request should be created");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MoreThanOneOpenPullRequestIsReportedAsync()
    {
        this.OpenPullRequests(
            Entry(),
            Entry(url: "https://github.com/credfeto/credfeto-workflow-mcp/pull/94", number: 94)
        );
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "more than one open pull request exists for the --head in " + REPOSITORY,
            actual: exception.Message
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("https://github.com/credfeto/credfeto-workflow-mcp/issues/93", PR)]
    [InlineData(PR_URL, 94)]
    public async Task AnOpenPullRequestInAnUnexpectedShapeIsReportedAsync(string url, int number)
    {
        this.OpenPullRequests(Entry(url: url, number: number));
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "unexpected result for the open pull request for the --head in " + REPOSITORY,
            actual: exception.Message
        );
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(BoardItemState.UNSET, null)]
    [InlineData(BoardItemState.NOT_ON_BOARD, null)]
    [InlineData(BoardItemState.STATE, WorkflowStates.NOT_STARTED)]
    [InlineData(BoardItemState.STATE, WorkflowStates.PLANNING)]
    [InlineData(BoardItemState.STATE, WorkflowStates.APPROVED)]
    public async Task AnExistingPullRequestBeforeDevelopmentIsMovedToDevelopmentAsync(
        BoardItemState state,
        string? name
    )
    {
        this.OpenPullRequests(Entry());
        this.BoardStatus(state: state, name: name);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.False(condition: response.Created, userMessage: "The existing pull request should be returned");
        await this.ReceivedBoardWriteAsync();
    }

    [Theory]
    [InlineData(WorkflowStates.DEVELOPMENT)]
    [InlineData(WorkflowStates.AI_SIMPLIFY)]
    [InlineData(WorkflowStates.AI_REVIEW)]
    [InlineData(WorkflowStates.HUMAN_REVIEW)]
    [InlineData(WorkflowStates.COMPLETE)]
    public async Task AnExistingPullRequestAtOrAfterDevelopmentIsNotMovedBackAsync(string name)
    {
        this.OpenPullRequests(Entry());
        this.BoardStatus(state: BoardItemState.STATE, name: name);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.False(condition: response.Created, userMessage: "The existing pull request should be returned");
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AnExistingPullRequestInAnUnknownStateIsReportedAsync()
    {
        this.OpenPullRequests(Entry());
        this.BoardStatus(state: BoardItemState.STATE, name: "Parked");
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateBoardWriteException exception = await Assert.ThrowsAsync<PrCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "the Workflow board status of the pull request is not a known workflow state; the pull request already exists: "
                + PR_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(PR_URL), actual: exception.Url);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AFailedBoardReadForAnExistingPullRequestIsReportedAsync()
    {
        this.OpenPullRequests(Entry());
        this.BoardReader.ReadStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.PULL_REQUEST,
                number: PR,
                project: Project,
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("boom"));
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateBoardWriteException exception = await Assert.ThrowsAsync<PrCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not put " + PR_URL + " on the Workflow board; the pull request already exists: " + PR_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(PR_URL), actual: exception.Url);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AFailedCreateWithNoOpenPullRequestIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(FailedCreateFlow);

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "could not create the pull request in " + REPOSITORY, actual: exception.Message);
        Assert.NotNull(exception.InnerException);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AFailedCreateWhoseRecoveryListFailsIsReportedAsync()
    {
        this.PullRequestList.ListAsync(
                repository: Arg.Any<string>(),
                issue: Arg.Any<int?>(),
                head: Arg.Any<string?>(),
                labels: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                _ => new PullRequestListResponse(Repository: REPOSITORY, PullRequests: []),
                _ => throw new PullRequestListFailedException("list failed")
            );
        using RecordingHttpMessageHandler upstream = new(FailedCreateFlow);

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "could not create the pull request in " + REPOSITORY, actual: exception.Message);
    }

    [Fact]
    public async Task AFailedCreateThatOpenedThePullRequestReturnsItAsync()
    {
        this.PullRequestList.ListAsync(
                repository: Arg.Any<string>(),
                issue: Arg.Any<int?>(),
                head: Arg.Any<string?>(),
                labels: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                new PullRequestListResponse(Repository: REPOSITORY, PullRequests: []),
                new PullRequestListResponse(Repository: REPOSITORY, PullRequests: [Entry()])
            );
        this.BoardStatus(state: BoardItemState.NOT_ON_BOARD, name: null);
        using RecordingHttpMessageHandler upstream = new(FailedCreateFlow);

        PrCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new Uri(PR_URL), actual: response.Url);
        Assert.False(condition: response.Created, userMessage: "The existing pull request should be returned");
        Assert.Equal(expected: EXISTING_NOTE, actual: response.Note);
        await this.ReceivedBoardWriteAsync();
    }

    [Fact]
    public async Task ACreatedPullRequestWithAnUnexpectedUrlIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => BranchFound(),
                1 => CreatedPullRequest("https://github.com/credfeto/credfeto-workflow-mcp/issues/93"),
                _ => Unexpected(index),
            }
        );

        PrCreateFailedException exception = await Assert.ThrowsAsync<PrCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "unexpected result for the new pull request, which may have been created: https://github.com/credfeto/credfeto-workflow-mcp/issues/93",
            actual: exception.Message
        );
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ABoardWriteFailureNamesTheCreatedPullRequestAsync()
    {
        this.BoardWriteThrows(new BoardWriteException("the board write was refused"));
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        PrCreateBoardWriteException exception = await Assert.ThrowsAsync<PrCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "the board write was refused; the pull request was already created: " + PR_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(PR_URL), actual: exception.Url);
    }

    [Fact]
    public async Task AnUpstreamBoardWriteFailureNamesTheCreatedPullRequestAsync()
    {
        this.BoardWriteThrows(new HttpRequestException("boom"));
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        PrCreateBoardWriteException exception = await Assert.ThrowsAsync<PrCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not put "
                + PR_URL
                + " on the Workflow board; the pull request was already created: "
                + PR_URL,
            actual: exception.Message
        );
    }

    // The head check passes, then every create attempt is rejected
    private static HttpResponseMessage FailedCreateFlow(int index)
    {
        return index == 0
            ? BranchFound()
            : Responses.StatusWithJson(
                statusCode: HttpStatusCode.UnprocessableEntity,
                json: "{\"message\":\"Validation Failed\"}"
            );
    }

    private void BoardWriteThrows(Exception exception)
    {
        this.BoardWriter.SetItemStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                fieldId: Arg.Any<string>(),
                fieldName: Arg.Any<string>(),
                optionId: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(exception);
    }
}
