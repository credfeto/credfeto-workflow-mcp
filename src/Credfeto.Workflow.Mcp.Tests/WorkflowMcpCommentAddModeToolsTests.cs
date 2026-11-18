using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpCommentAddModeToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const int ISSUE = 16;

    private const int PULL_REQUEST = 41;

    private const string BODY = "## Summary\n\nThe comment text.";

    private readonly ICommentAddService _service;

    private readonly IRequestToolMode _toolMode;

    public WorkflowMcpCommentAddModeToolsTests()
    {
        this._service = GetSubstitute<ICommentAddService>();
        this._toolMode = GetSubstitute<IRequestToolMode>();
        this._toolMode.Current.Returns(ToolModes.Issues);
    }

    [Fact]
    public async Task IssuesModeCommentsOnlyThroughTheIssueCheckAsync()
    {
        CommentAddResult expected = new(
            Outcome: CommentAddOutcome.Posted,
            Message: "the comment was posted on " + REPOSITORY + "#16",
            Url: new("https://github.com/" + REPOSITORY + "/issues/16#issuecomment-99"),
            Rule: null,
            LineNumber: null
        );
        this._service.AddToIssueAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                body: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        CommentAddResult actual = await this.CommentAddAsync(pr: null, issue: ISSUE);

        Assert.Same(expected: expected, actual: actual);
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .AddAsync(
                repository: default!,
                pullRequest: default,
                issue: default,
                body: default!,
                cancellationToken: this.CancellationToken()
            );
    }

    [Fact]
    public async Task IssuesModeRefusesAPullRequestTargetAsync()
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.CommentAddAsync(pr: PULL_REQUEST, issue: null)
        );

        Assert.Equal(
            expected: "comment_add cannot comment on a pull request in X-Mode Issues; give an issue number",
            actual: exception.Message
        );
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .AddToIssueAsync(
                repository: default!,
                issue: default,
                body: default!,
                cancellationToken: this.CancellationToken()
            );
    }

    [Fact]
    public async Task IssuesModeReportsAnIssueThatIsAPullRequestAsync()
    {
        ParameterValidationException failure = ParameterValidationException.For(
            parameterName: "issue",
            rule: "must be an issue, not a pull request"
        );
        this._service.AddToIssueAsync(
                repository: REPOSITORY,
                issue: PULL_REQUEST,
                body: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.CommentAddAsync(pr: null, issue: PULL_REQUEST)
        );

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private Task<CommentAddResult> CommentAddAsync(int? pr, int? issue)
    {
        return WorkflowMcpTools.CommentAddAsync(
            repo: REPOSITORY,
            body: BODY,
            service: this._service,
            toolMode: this._toolMode,
            pr: pr,
            issue: issue,
            cancellationToken: this.CancellationToken()
        );
    }
}
