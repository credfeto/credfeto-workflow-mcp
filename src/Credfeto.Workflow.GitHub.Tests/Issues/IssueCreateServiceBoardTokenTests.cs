using System;
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

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueCreateServiceBoardTokenTests : IssueCreateServiceTestBase
{
    private const string NODE_ID = "I_kwDOABCDEF";

    private const string NOTE =
        "the caller's token could not put the issue on the Workflow board, so the server's board token added it as Not Started";

    [Fact]
    public async Task ACallerWhoCanSeeTheBoardNeverUsesTheBoardTokenAsync()
    {
        this.BoardTokenIsConfigured();
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new IssueCreateResponse(Url: new Uri(ISSUE_URL), Note: null), actual: response);
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
        await this.DidNotUseTheBoardTokenAsync();
    }

    [Fact]
    public async Task ABoardTheCallerCannotSeeIsCheckedAndWrittenWithTheBoardTokenAsync()
    {
        this.BoardTokenIsConfigured();
        this.CallerResolveFailsWith(new WorkflowBoardNotFoundException("no project titled Workflow"));
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new IssueCreateResponse(Url: new Uri(ISSUE_URL), Note: NOTE), actual: response);
        await this
            .BoardFallbackWriter.Received(1)
            .CheckNotStartedAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
        await this
            .BoardFallbackWriter.Received(1)
            .AddAsNotStartedAsync(
                repository: REPOSITORY,
                contentNodeId: NODE_ID,
                url: new Uri(ISSUE_URL),
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("FORBIDDEN", "Resource not accessible by integration")]
    [InlineData("INSUFFICIENT_SCOPES", "Your token has not been granted the required scopes")]
    [InlineData("NOT_FOUND", "Could not resolve to a ProjectV2 with the number 7.")]
    public async Task AStatusFieldTheCallerIsRefusedIsCheckedWithTheBoardTokenAsync(string type, string message)
    {
        this.BoardTokenIsConfigured();
        this.StatusResolver.ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: WorkflowStates.NOT_STARTED,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new StatusFieldReadFailedException(
                    message: "could not read the status fields of project 7",
                    innerException: Refused(type: type, message: message)
                )
            );
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: NOTE, actual: response.Note);
        await this
            .BoardFallbackWriter.Received(1)
            .CheckNotStartedAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task WithNoBoardTokenABoardTheCallerCannotSeeFailsAsBeforeAsync()
    {
        WorkflowBoardNotFoundException failure = new("no project titled Workflow");
        this.CallerResolveFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        Assert.Empty(upstream.Requests);
        await this.DidNotUseTheBoardTokenAsync();
    }

    [Fact]
    public async Task AResolveFailureThatIsNotARefusalNeverUsesTheBoardTokenAsync()
    {
        this.BoardTokenIsConfigured();
        WorkflowBoardAmbiguousException failure = new("more than one project titled Workflow");
        this.CallerResolveFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        WorkflowBoardAmbiguousException exception = await Assert.ThrowsAsync<WorkflowBoardAmbiguousException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        await this.DidNotUseTheBoardTokenAsync();
    }

    [Fact]
    public async Task ABoardTheBoardTokenCannotSeeEitherFailsBeforeAnythingIsWrittenAsync()
    {
        this.BoardTokenIsConfigured();
        this.CallerResolveFailsWith(new WorkflowBoardNotFoundException("no project titled Workflow"));
        WorkflowBoardNotFoundException failure = new("no project titled Workflow is linked to " + REPOSITORY);
        this.BoardFallbackWriter.CheckNotStartedAsync(
                repository: REPOSITORY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        Assert.Empty(upstream.Requests);
        await this.DidNotAddWithTheBoardTokenAsync();
    }

    [Fact]
    public async Task ABoardWriteTheCallerIsRefusedIsRetriedWithTheBoardTokenAsync()
    {
        this.BoardTokenIsConfigured();
        this.CallerWriteFailsWith(
            new BoardWriteException(
                message: "could not add " + ISSUE_URL + " to project 7",
                innerException: Refused(type: "FORBIDDEN", message: "Resource not accessible by integration")
            )
        );
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new IssueCreateResponse(Url: new Uri(ISSUE_URL), Note: NOTE), actual: response);
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
        await this
            .BoardFallbackWriter.Received(1)
            .AddAsNotStartedAsync(
                repository: REPOSITORY,
                contentNodeId: NODE_ID,
                url: new Uri(ISSUE_URL),
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this.DidNotCheckWithTheBoardTokenAsync();
    }

    [Fact]
    public async Task ABoardWriteFailureThatIsNotARefusalNeverUsesTheBoardTokenAsync()
    {
        this.BoardTokenIsConfigured();
        HttpRequestException failure = new("upstream detail");
        this.CallerWriteFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not put "
                + ISSUE_URL
                + " on the Workflow board; the issue was already created: "
                + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
        await this.DidNotUseTheBoardTokenAsync();
    }

    [Fact]
    public async Task WithNoBoardTokenARefusedBoardWriteFailsAsBeforeAsync()
    {
        BoardWriteException failure = new(
            message: "could not add " + ISSUE_URL + " to project 7",
            innerException: Refused(type: "FORBIDDEN", message: "Resource not accessible by integration")
        );
        this.CallerWriteFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not add " + ISSUE_URL + " to project 7; the issue was already created: " + ISSUE_URL,
            actual: exception.Message
        );
        await this.DidNotUseTheBoardTokenAsync();
    }

    [Fact]
    public async Task WhenTheBoardTokenWriteAlsoFailsTheIssueIsReportedAsCreatedButNotOnTheBoardAsync()
    {
        this.BoardTokenIsConfigured();
        this.CallerResolveFailsWith(new WorkflowBoardNotFoundException("no project titled Workflow"));
        BoardWriteException failure = new(
            "could not add " + ISSUE_URL + " to the Workflow board as Not Started with the board token"
        );
        this.BoardFallbackWriter.AddAsNotStartedAsync(
                repository: REPOSITORY,
                contentNodeId: NODE_ID,
                url: new Uri(ISSUE_URL),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = new(CreatedWithNodeIdFlow);

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not add "
                + ISSUE_URL
                + " to the Workflow board as Not Started with the board token; the issue was created but is not on the Workflow board: "
                + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: exception.Url);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task AnIssueWithoutANodeIdIsHandedToTheBoardTokenWriterToRefuseAsync()
    {
        this.BoardTokenIsConfigured();
        this.CallerResolveFailsWith(new WorkflowBoardNotFoundException("no project titled Workflow"));
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        await this.CreateAsync(upstream: upstream);

        await this
            .BoardFallbackWriter.Received(1)
            .AddAsNotStartedAsync(
                repository: REPOSITORY,
                contentNodeId: string.Empty,
                url: new Uri(ISSUE_URL),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private static HttpResponseMessage CreatedWithNodeIdFlow(int index)
    {
        return index switch
        {
            0 => Responses.Json("""[{"name":"Medium"}]"""),
            1 => Responses.Json("{\"html_url\":\"" + ISSUE_URL + "\",\"node_id\":\"" + NODE_ID + "\"}"),
            _ => Unexpected(index),
        };
    }

    private static GitHubGraphQlException Refused(string type, string message)
    {
        return new("GitHub GraphQL request returned errors") { Errors = [message], ErrorTypes = [type] };
    }

    private void BoardTokenIsConfigured()
    {
        this.BoardFallbackWriter.IsConfigured.Returns(true);
    }

    private void CallerResolveFailsWith(Exception failure)
    {
        this.BoardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private void CallerWriteFailsWith(Exception failure)
    {
        this.BoardWriter.SetItemStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.ISSUE,
                number: ISSUE,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private async Task DidNotUseTheBoardTokenAsync()
    {
        await this.DidNotCheckWithTheBoardTokenAsync();
        await this.DidNotAddWithTheBoardTokenAsync();
    }

    private Task DidNotCheckWithTheBoardTokenAsync()
    {
        return this
            .BoardFallbackWriter.DidNotReceiveWithAnyArgs()
            .CheckNotStartedAsync(repository: default!, cancellationToken: default)
            .AsTask();
    }

    private Task DidNotAddWithTheBoardTokenAsync()
    {
        return this
            .BoardFallbackWriter.DidNotReceiveWithAnyArgs()
            .AddAsNotStartedAsync(
                repository: default!,
                contentNodeId: default!,
                url: default!,
                cancellationToken: default
            )
            .AsTask();
    }
}
