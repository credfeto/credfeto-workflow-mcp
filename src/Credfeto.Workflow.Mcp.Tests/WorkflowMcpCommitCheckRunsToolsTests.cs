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

public sealed class WorkflowMcpCommitCheckRunsToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private readonly ICommitCheckRunsService _service;

    public WorkflowMcpCommitCheckRunsToolsTests()
    {
        this._service = GetSubstitute<ICommitCheckRunsService>();
    }

    [Fact]
    public async Task CommitCheckRunsReturnsWhatTheServiceReadAsync()
    {
        CommitCheckRunsResponse expected = new(
            Repo: REPOSITORY,
            Sha: SHA,
            Running: 1,
            CheckRuns: [new(Id: 1, Name: "build", Status: "queued", Conclusion: null, AppId: 15368)]
        );
        this.ReadReturnsAsync().Returns(expected);

        CommitCheckRunsResponse actual = await this.CommitCheckRunsAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task CommitCheckRunsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in CommitCheckRunsFailures())
        {
            this.ReadReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.CommitCheckRunsAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task CommitCheckRunsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ReadReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.CommitCheckRunsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> CommitCheckRunsFailures()
    {
        return
        [
            ParameterValidationException.For(
                parameterName: "sha",
                rule: "it must be a full 40 character hexadecimal commit SHA"
            ),
            new CommitNotFoundException("the commit " + SHA + " was not found in " + REPOSITORY),
            new PrChecksUnreadableException("could not read the check runs of " + REPOSITORY + " commit " + SHA),
        ];
    }

    private ValueTask<CommitCheckRunsResponse> ReadReturnsAsync()
    {
        return this._service.ReadAsync(
            repository: REPOSITORY,
            sha: SHA,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<CommitCheckRunsResponse> CommitCheckRunsAsync()
    {
        return WorkflowMcpTools.CommitCheckRunsAsync(
            repo: REPOSITORY,
            sha: SHA,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
