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

public sealed class WorkflowStatusSetServiceTests : TestBase
{
    private const string OWNER = "credfeto";
    private const string NAME = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + NAME;
    private const int PULL_REQUEST = 81;
    private const int ISSUE = 13;
    private const string FIELD_ID = "PVTSSF_1";
    private const string FIELD_NAME = "Status";
    private const string OPTION_ID = "OPT_1";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IWorkflowStatusSetService _service;
    private readonly IWorkflowStatusResolver _statusResolver;

    public WorkflowStatusSetServiceTests()
    {
        this._boardResolver = GetSubstitute<IWorkflowBoardResolver>();
        this._statusResolver = GetSubstitute<IWorkflowStatusResolver>();
        this._boardWriter = GetSubstitute<IBoardWriter>();

        this._service = new WorkflowStatusSetService(
            boardResolver: this._boardResolver,
            statusResolver: this._statusResolver,
            boardWriter: this._boardWriter
        );

        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);
        this._statusResolver.ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ResolvedStatusOption(FieldId: FIELD_ID, FieldName: FIELD_NAME, OptionId: OPTION_ID));
    }

    [Fact]
    public async Task APullRequestIsMovedToTheStateAsync()
    {
        WorkflowStatusSetResponse response = await this.SetAsync(
            pullRequest: PULL_REQUEST,
            issue: null,
            status: WorkflowStates.DEVELOPMENT
        );

        Assert.Equal(
            expected: new WorkflowStatusSetResponse(
                ItemUrl: new(
                    uriString: "https://github.com/credfeto/credfeto-workflow-mcp/pull/81",
                    uriKind: UriKind.Absolute
                ),
                Status: WorkflowStates.DEVELOPMENT
            ),
            actual: response
        );
        await this.ReceivedWriteAsync(kind: WorkItemKind.PULL_REQUEST, number: PULL_REQUEST);
    }

    [Fact]
    public async Task AnIssueIsMovedToTheStateAsync()
    {
        WorkflowStatusSetResponse response = await this.SetAsync(
            pullRequest: null,
            issue: ISSUE,
            status: WorkflowStates.PLANNING
        );

        Assert.Equal(
            expected: new WorkflowStatusSetResponse(
                ItemUrl: new(
                    uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/13",
                    uriKind: UriKind.Absolute
                ),
                Status: WorkflowStates.PLANNING
            ),
            actual: response
        );
        await this.ReceivedWriteAsync(kind: WorkItemKind.ISSUE, number: ISSUE);
    }

    [Fact]
    public async Task TheStatusIsMatchedRegardlessOfCaseAsync()
    {
        WorkflowStatusSetResponse response = await this.SetAsync(
            pullRequest: PULL_REQUEST,
            issue: null,
            status: "ai review"
        );

        Assert.Equal(expected: WorkflowStates.AI_REVIEW, actual: response.Status);
        await this
            ._statusResolver.Received(1)
            .ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: WorkflowStates.AI_REVIEW,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(PULL_REQUEST, ISSUE)]
    public async Task AnythingButExactlyOneWorkItemIsRejectedAsync(int? pullRequest, int? issue)
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            issue: issue,
            status: WorkflowStates.DEVELOPMENT
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
            issue: null,
            status: WorkflowStates.DEVELOPMENT
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
            issue: issue,
            status: WorkflowStates.DEVELOPMENT
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
            issue: null,
            status: WorkflowStates.DEVELOPMENT
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }

    [Fact]
    public async Task AnEmptyStatusIsRejectedAsync()
    {
        ParameterValidationException exception = await this.AssertInvalidAsync(
            repository: REPOSITORY,
            pullRequest: PULL_REQUEST,
            issue: null,
            status: ""
        );

        Assert.Equal(expected: "status", actual: exception.ParameterName);
    }

    [Fact]
    public async Task AnUnknownStatusIsRejectedAsync()
    {
        _ = await Assert.ThrowsAsync<UnknownWorkflowStateException>(() =>
            this.SetAsync(pullRequest: PULL_REQUEST, issue: null, status: "Testing").AsTask()
        );

        await this.DidNotResolveBoardAsync();
    }

    [Fact]
    public async Task ABoardThatCannotBeResolvedPropagatesAsync()
    {
        WorkflowBoardNotFoundException failure = new("no board");
        this._boardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.SetAsync(pullRequest: PULL_REQUEST, issue: null, status: WorkflowStates.DEVELOPMENT).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
        await this.DidNotWriteAsync();
    }

    [Fact]
    public async Task AStatusOptionThatCannotBeResolvedPropagatesAsync()
    {
        StatusOptionMissingException failure = new("no option");
        this._statusResolver.ResolveAsync(
                repository: Arg.Any<string>(),
                project: Arg.Any<WorkflowProject>(),
                state: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        StatusOptionMissingException exception = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.SetAsync(pullRequest: PULL_REQUEST, issue: null, status: WorkflowStates.DEVELOPMENT).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
        await this.DidNotWriteAsync();
    }

    [Fact]
    public async Task AWriteThatFailsPropagatesAsync()
    {
        BoardWriteException failure = new("write failed");
        this._boardWriter.SetItemStatusAsync(
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
            .ThrowsAsync(failure);

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            this.SetAsync(pullRequest: PULL_REQUEST, issue: null, status: WorkflowStates.DEVELOPMENT).AsTask()
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task<ParameterValidationException> AssertInvalidAsync(
        string repository,
        int? pullRequest,
        int? issue,
        string status
    )
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this
                ._service.SetAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    issue: issue,
                    status: status,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        await this.DidNotResolveBoardAsync();

        return exception;
    }

    private Task DidNotResolveBoardAsync()
    {
        return this
            ._boardResolver.DidNotReceive()
            .ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .AsTask();
    }

    private Task DidNotWriteAsync()
    {
        return this
            ._boardWriter.DidNotReceive()
            .SetItemStatusAsync(
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
            .AsTask();
    }

    private Task ReceivedWriteAsync(WorkItemKind kind, int number)
    {
        return this
            ._boardWriter.Received(1)
            .SetItemStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: kind,
                number: number,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .AsTask();
    }

    private ValueTask<WorkflowStatusSetResponse> SetAsync(int? pullRequest, int? issue, string status)
    {
        return this._service.SetAsync(
            repository: REPOSITORY,
            pullRequest: pullRequest,
            issue: issue,
            status: status,
            cancellationToken: this.CancellationToken()
        );
    }
}
