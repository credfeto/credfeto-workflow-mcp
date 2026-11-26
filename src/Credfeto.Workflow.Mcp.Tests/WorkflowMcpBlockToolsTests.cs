using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpBlockToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int NUMBER = 174;
    private const string BODY = "Which image should this use?";

    private readonly IBlockService _service;

    public WorkflowMcpBlockToolsTests()
    {
        this._service = GetSubstitute<IBlockService>();
    }

    private static BlockResult Blocked()
    {
        return new(
            Outcome: BlockOutcome.Blocked,
            Message: "the comment was posted on " + REPOSITORY + "#174 and Blocked was added",
            CommentUrl: new Uri(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1",
                uriKind: UriKind.Absolute
            ),
            AlreadyBlocked: false,
            Rule: null,
            LineNumber: null
        );
    }

    private static IReadOnlyList<Exception> ReportedFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "issue", rule: "must be above 0"),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new RepositoryVisibilityLookupFailedException("could not check the visibility of " + REPOSITORY),
            new LeakCheckFailedException("the leak check could not be completed"),
        ];
    }

    private ValueTask<BlockResult> IssueCallAsync()
    {
        return this._service.BlockIssueAsync(
            repository: REPOSITORY,
            issue: NUMBER,
            body: BODY,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private ValueTask<BlockResult> PullRequestCallAsync()
    {
        return this._service.BlockPullRequestAsync(
            repository: REPOSITORY,
            pullRequest: NUMBER,
            body: BODY,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<BlockResult> IssueBlockAsync()
    {
        return WorkflowMcpTools.IssueBlockAsync(
            repo: REPOSITORY,
            issue: NUMBER,
            body: BODY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }

    private Task<BlockResult> PrBlockAsync()
    {
        return WorkflowMcpTools.PrBlockAsync(
            repo: REPOSITORY,
            pr: NUMBER,
            body: BODY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task IssueBlockReturnsWhatTheServiceDidAsync()
    {
        BlockResult expected = Blocked();
        this.IssueCallAsync().Returns(expected);

        BlockResult actual = await this.IssueBlockAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrBlockReturnsWhatTheServiceDidAsync()
    {
        BlockResult expected = Blocked();
        this.PullRequestCallAsync().Returns(expected);

        BlockResult actual = await this.PrBlockAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueBlockFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in ReportedFailures())
        {
            this.IssueCallAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueBlockAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrBlockFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in ReportedFailures())
        {
            this.PullRequestCallAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.PrBlockAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task IssueBlockOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueCallAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueBlockAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task PrBlockOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PullRequestCallAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrBlockAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }
}
