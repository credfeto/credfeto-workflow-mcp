using System.Collections.Generic;
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

public sealed class WorkflowStatusCheckServiceListTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private static readonly WorkflowProject Board = new(ProjectNodeId: "PVT_board", Number: 3, Owner: "credfeto");

    private readonly IWorkflowBoardReader _reader;
    private readonly IWorkflowBoardResolver _resolver;

    public WorkflowStatusCheckServiceListTests()
    {
        this._resolver = GetSubstitute<IWorkflowBoardResolver>();
        this._reader = GetSubstitute<IWorkflowBoardReader>();
    }

    private WorkflowStatusCheckService CreateService()
    {
        return new(boardResolver: this._resolver, boardReader: this._reader);
    }

    private void ResolvesTheBoard()
    {
        this._resolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Board);
    }

    private ValueTask<IReadOnlyList<BoardListedItem>> ListItemsCallAsync()
    {
        return this._reader.ListItemsAsync(
            project: Board,
            field: Arg.Any<WorkflowStatusField>(),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task EveryItemIsReturnedWithItsStateAsync()
    {
        this.ResolvesTheBoard();
        this.ListItemsCallAsync()
            .Returns([
                new BoardListedItem(
                    Kind: WorkItemKind.ISSUE,
                    Repository: REPOSITORY,
                    Number: 1,
                    Status: new(State: BoardItemState.STATE, Name: "Approved")
                ),
                new BoardListedItem(
                    Kind: WorkItemKind.PULL_REQUEST,
                    Repository: "credfeto/other",
                    Number: 2,
                    Status: new(State: BoardItemState.UNSET, Name: null)
                ),
            ]);

        WorkflowBoardItems result = await this.CreateService()
            .ListAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(expected: 2, actual: result.Total);
        Assert.Equal(
            expected:
            [
                new WorkflowBoardItem(
                    Kind: WorkflowBoardItemKind.Issue,
                    Repository: REPOSITORY,
                    Number: 1,
                    State: "Approved"
                ),
                new WorkflowBoardItem(
                    Kind: WorkflowBoardItemKind.PullRequest,
                    Repository: "credfeto/other",
                    Number: 2,
                    State: null
                ),
            ],
            actual: result.Items
        );
    }

    [Fact]
    public async Task WorkflowStatesAreJudgedAsTheStatusCheckDoesAsync()
    {
        this.ResolvesTheBoard();
        this.ListItemsCallAsync().Returns([]);

        WorkflowBoardItems result = await this.CreateService()
            .ListAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());

        Assert.Empty(result.Items);
        Assert.Equal(expected: 0, actual: result.Total);
        _ = await this
            ._reader.Received(1)
            .ListItemsAsync(
                project: Board,
                field: Arg.Is<WorkflowStatusField>(field =>
                    field.OptionIds.Count == WorkflowStates.All.Count
                    && field.OptionIds.ContainsKey("Approved")
                    && field.OptionIds.ContainsKey("Development")
                ),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MalformedRepositoryIsRejectedBeforeTheBoardIsResolvedAsync()
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService().ListAsync(repository: "credfeto", cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        _ = await this
            ._resolver.DidNotReceiveWithAnyArgs()
            .ResolveAsync(repository: default!, cancellationToken: this.CancellationToken());
    }

    [Fact]
    public async Task BoardResolutionFailureIsPassedOnAsync()
    {
        WorkflowBoardNotFoundException failure = new("no project titled Workflow is linked to " + REPOSITORY);
        this._resolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CreateService().ListAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task ListingFailureIsPassedOnAsync()
    {
        this.ResolvesTheBoard();
        BoardReadFailedException failure = new("could not list the items of project 3");
        this.ListItemsCallAsync().ThrowsAsync(failure);

        BoardReadFailedException exception = await Assert.ThrowsAsync<BoardReadFailedException>(() =>
            this.CreateService().ListAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
    }
}
