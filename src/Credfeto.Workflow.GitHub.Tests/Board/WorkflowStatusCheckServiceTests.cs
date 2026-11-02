using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class WorkflowStatusCheckServiceTests : TestBase
{
    private const string OWNER = "credfeto";
    private const string NAME = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + NAME;
    private const int PULL_REQUEST = 81;
    private const int ISSUE = 13;

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    private readonly IWorkflowBoardReader _boardReader;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IWorkflowStatusCheckService _service;

    public WorkflowStatusCheckServiceTests()
    {
        this._boardResolver = GetSubstitute<IWorkflowBoardResolver>();
        this._boardReader = GetSubstitute<IWorkflowBoardReader>();

        this._service = new WorkflowStatusCheckService(
            boardResolver: this._boardResolver,
            boardReader: this._boardReader
        );

        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);
    }

    [Fact]
    public async Task APullRequestHoldingAStateIsFoundAsync()
    {
        this.BoardHolds(new(State: BoardItemState.STATE, Name: "Development"));

        WorkflowStatusCheckResult result = await this.CheckAsync(pullRequest: PULL_REQUEST, issue: null);

        Assert.Equal(
            expected: new WorkflowStatusCheckResult(
                Outcome: WorkflowStatusCheckOutcome.Found,
                State: "Development",
                Message: null
            ),
            actual: result
        );
        await this.ReceivedReadAsync(kind: WorkItemKind.PULL_REQUEST, number: PULL_REQUEST);
    }

    [Fact]
    public async Task AnIssueWithNoStateIsUnsetAsync()
    {
        this.BoardHolds(new(State: BoardItemState.UNSET, Name: null));

        WorkflowStatusCheckResult result = await this.CheckAsync(pullRequest: null, issue: ISSUE);

        Assert.Equal(
            expected: new WorkflowStatusCheckResult(
                Outcome: WorkflowStatusCheckOutcome.Unset,
                State: null,
                Message: null
            ),
            actual: result
        );
        await this.ReceivedReadAsync(kind: WorkItemKind.ISSUE, number: ISSUE);
    }

    [Fact]
    public async Task AWorkItemMissingFromTheBoardIsNotOnBoardAsync()
    {
        this.BoardHolds(new(State: BoardItemState.NOT_ON_BOARD, Name: null));

        WorkflowStatusCheckResult result = await this.CheckAsync(pullRequest: PULL_REQUEST, issue: null);

        Assert.Equal(
            expected: new WorkflowStatusCheckResult(
                Outcome: WorkflowStatusCheckOutcome.NotOnBoard,
                State: null,
                Message: "credfeto/credfeto-workflow-mcp#81 is not on the Workflow board"
            ),
            actual: result
        );
    }

    [Fact]
    public async Task TheStatusFieldRecognisesEveryWorkflowStateAsync()
    {
        WorkflowStatusField? field = null;
        this._boardReader.ReadStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                field: Arg.Do<WorkflowStatusField>(f => field = f),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new BoardItemStatus(State: BoardItemState.UNSET, Name: null));

        _ = await this.CheckAsync(pullRequest: PULL_REQUEST, issue: null);

        Assert.NotNull(field);
        Assert.Equal(expected: WorkflowStates.All, actual: field.OptionIds.Keys, comparer: StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(PULL_REQUEST, ISSUE)]
    public async Task AnythingButExactlyOneWorkItemIsRejectedAsync(int? pullRequest, int? issue)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            issue: issue
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Equal(expected: "exactly one of pr or issue must be given", actual: exception.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task APullRequestNumberThatIsNotAboveZeroIsRejectedAsync(int pullRequest)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            issue: null
        );

        Assert.Equal(expected: "pullRequest", actual: exception.ParameterName);
        Assert.Equal(expected: "must be above 0", actual: exception.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AnIssueNumberThatIsNotAboveZeroIsRejectedAsync(int issue)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: null,
            issue: issue
        );

        Assert.Equal(expected: "issue", actual: exception.ParameterName);
        Assert.Equal(expected: "must be above 0", actual: exception.Rule);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task AnInvalidRepositoryIsRejectedAsync(string repository)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: repository,
            pullRequest: PULL_REQUEST,
            issue: null
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }

    [Fact]
    public async Task ABoardThatCannotBeResolvedPropagatesAsync()
    {
        WorkflowBoardNotFoundException failure = new("no board");
        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CheckAsync(pullRequest: PULL_REQUEST, issue: null).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task ABoardThatCannotBeReadPropagatesAsync()
    {
        BoardReadFailedException failure = new("unreadable");
        this._boardReader.ReadStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.CheckAsync(pullRequest: PULL_REQUEST, issue: null).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task<ParameterValidationException> AssertInvalidAsync(string repository, int? pullRequest, int? issue)
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this
                ._service.CheckAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    issue: issue,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        await this
            ._boardResolver.DidNotReceive()
            .ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());

        return exception;
    }

    private void BoardHolds(BoardItemStatus status)
    {
        this._boardReader.ReadStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(status);
    }

    private Task ReceivedReadAsync(WorkItemKind kind, int number)
    {
        return this
            ._boardReader.Received(1)
            .ReadStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: kind,
                number: number,
                project: Project,
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .AsTask();
    }

    private ValueTask<WorkflowStatusCheckResult> CheckAsync(int? pullRequest, int? issue)
    {
        return this._service.CheckAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            issue: issue,
            cancellationToken: this.CancellationToken()
        );
    }
}
