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

public sealed class WorkflowMcpPrStatusToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 73;
    private const string REFERENCE = REPOSITORY + "#73";

    private readonly IPrStatusService _service;

    public WorkflowMcpPrStatusToolsTests()
    {
        this._service = GetSubstitute<IPrStatusService>();
    }

    [Fact]
    public async Task PrStatusReturnsWhatTheServiceReadAsync()
    {
        PrStatusResponse expected = new(
            Pr: REFERENCE,
            State: "OPEN",
            Draft: "no",
            Title: "Add the status tool",
            Url: new("https://github.com/" + REPOSITORY + "/pull/73"),
            Base: "main",
            Head: "feat/73-pr-status-tool",
            HeadSha: "0123456789abcdef0123456789abcdef01234567",
            AutoMerge: "off",
            ReviewDecision: "REVIEW_REQUIRED",
            MergeState: "BLOCKED",
            Mergeable: "MERGEABLE",
            Labels: string.Empty,
            Assignees: string.Empty,
            Author: "credfeto",
            ChangedFiles: 3,
            Board: null,
            ReadyAt: "2026-01-01T00:00:00Z",
            ReadySource: "created",
            Checks: new(Verdict: PrChecksVerdict.NoChecks, Failing: [], Pending: []),
            Activity: new(Comments: 0, Reviews: 0, Threads: 0, Unresolved: 0, SinceReady: 0, Last: null)
        );
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrStatusResponse actual = await this.PrStatusAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrStatusFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in PrStatusFailures())
        {
            this.PrStatusFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.PrStatusAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrStatusOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrStatusFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrStatusAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> PrStatusFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "pullRequest", rule: "must be above 0"),
            new PullRequestNotFoundException("pull request " + REFERENCE + " was not found"),
            new PullRequestViewFailedException("could not read pull request " + REFERENCE),
            new PullRequestCommentsIncompleteException("could not read all the comments of " + REFERENCE),
            new PrStatusHeadChangedException(
                "the head of pull request " + REFERENCE + " changed while its status was being read"
            ),
            new PrReadyAtFailedException("could not read when pull request " + REFERENCE + " became ready for review"),
            new CommitNotFoundException("commit was not found"),
            new BranchNotFoundException("branch was not found"),
            new RepositoryNotFoundException("repository was not found"),
            new RequiredChecksUnreadableException("could not read the required checks"),
            new PrChecksUnreadableException("could not read the checks"),
            new WorkflowBoardAmbiguousException("more than one board"),
            new WorkflowBoardInvalidException("the board is invalid"),
            new BoardReadFailedException("could not read the board"),
        ];
    }

    private void PrStatusFailsWith(Exception failure)
    {
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrStatusResponse> PrStatusAsync()
    {
        return WorkflowMcpTools.PrStatusAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
