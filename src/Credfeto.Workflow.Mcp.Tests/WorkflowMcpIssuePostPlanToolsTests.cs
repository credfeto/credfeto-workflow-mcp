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

public sealed class WorkflowMcpIssuePostPlanToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 173;
    private const string BODY = "## Implementation Plan\n\nDo the work.";
    private const string COMMENT_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/173#issuecomment-1";

    private readonly IIssuePostPlanService _service;

    public WorkflowMcpIssuePostPlanToolsTests()
    {
        this._service = GetSubstitute<IIssuePostPlanService>();
    }

    [Fact]
    public async Task IssuePostPlanReturnsWhatTheServiceDidAsync()
    {
        IssuePostPlanResponse expected = new(
            CommentUrl: new Uri(uriString: COMMENT_URL, uriKind: UriKind.Absolute),
            Labels: ["Blocked"],
            Board: "Planning"
        );
        this._service.PostAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                body: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssuePostPlanResponse actual = await this.IssuePostPlanAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssuePostPlanFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in IssuePostPlanFailures())
        {
            this.IssuePostPlanFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.IssuePostPlanAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task IssuePostPlanOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssuePostPlanFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssuePostPlanAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> IssuePostPlanFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "issue", rule: "must be above 0"),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new RepositoryVisibilityLookupFailedException("could not check the visibility of " + REPOSITORY),
            new LeakCheckFailedException("the leak check could not be completed"),
            new IssueNotFoundException("issue " + REPOSITORY + "#173 was not found"),
            new IssueViewFailedException("could not read issue " + REPOSITORY + "#173"),
            new IssuePostPlanRefusedException("post-plan refused: On Hold label") { Rule = "On Hold label" },
            new IssuePostPlanFailedException(
                "could not add the Blocked label; steps done: comment posted " + COMMENT_URL
            )
            {
                CommentUrl = new Uri(uriString: COMMENT_URL, uriKind: UriKind.Absolute),
                StepsDone = ["comment posted " + COMMENT_URL],
            },
        ];
    }

    private void IssuePostPlanFailsWith(Exception failure)
    {
        this._service.PostAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                body: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssuePostPlanResponse> IssuePostPlanAsync()
    {
        return WorkflowMcpTools.IssuePostPlanAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            body: BODY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
