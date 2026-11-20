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

public sealed class WorkflowMcpPrSyncLabelsToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 87;
    private const string REFERENCE = REPOSITORY + "#87";

    private readonly IPrSyncLabelsService _service;

    public WorkflowMcpPrSyncLabelsToolsTests()
    {
        this._service = GetSubstitute<IPrSyncLabelsService>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrSyncLabelsReturnsWhatTheServiceAnsweredAsync(bool assignSelf)
    {
        PrSyncLabelsResult expected = new(
            Outcome: PrSyncLabelsOutcome.Synced,
            Message: REFERENCE + " synced",
            Url: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87"),
            Added: ["bug"],
            Skipped: [],
            FailedReferences: [],
            Assigned: assignSelf,
            Rule: null
        );
        this._service.SyncAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                assignSelf: assignSelf,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrSyncLabelsResult actual = await this.PrSyncLabelsAsync(assignSelf);

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrSyncLabelsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in Failures())
        {
            this.FailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => this.PrSyncLabelsAsync(false));

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrSyncLabelsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.FailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.PrSyncLabelsAsync(false)
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> Failures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "pr", rule: "must be above 0"),
            new PullRequestNotFoundException(
                "the pull request " + REFERENCE + " was not found, or the caller cannot see it"
            ),
            new ClosingIssueLabelsFailedException("could not read the closing issues of " + REFERENCE),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new LabelListFailedException("could not list the labels of " + REPOSITORY),
            new PrSyncLabelsFailedException(
                "could not read the pull request " + REFERENCE + ", so nothing was written"
            ),
        ];
    }

    private void FailsWith(Exception failure)
    {
        this._service.SyncAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                assignSelf: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrSyncLabelsResult> PrSyncLabelsAsync(bool assignSelf)
    {
        return WorkflowMcpTools.PrSyncLabelsAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._service,
            assignSelf: assignSelf,
            cancellationToken: this.CancellationToken()
        );
    }
}
