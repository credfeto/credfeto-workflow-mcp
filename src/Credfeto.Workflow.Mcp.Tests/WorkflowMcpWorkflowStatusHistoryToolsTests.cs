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

public sealed class WorkflowMcpWorkflowStatusHistoryToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IWorkflowStatusHistoryReader _reader;

    public WorkflowMcpWorkflowStatusHistoryToolsTests()
    {
        this._reader = GetSubstitute<IWorkflowStatusHistoryReader>();
    }

    [Fact]
    public async Task WorkflowStatusHistoryReturnsWhatTheReaderReadAsync()
    {
        WorkflowStatusHistoryResult expected = new(
            Repository: REPOSITORY,
            Number: 7,
            Changes:
            [
                new(
                    Time: "2026-01-02T03:04:05Z",
                    Actor: "credfeto",
                    PreviousStatus: "Development",
                    Status: "AI Review"
                ),
            ]
        );
        this._reader.ReadAsync(
                repository: REPOSITORY,
                issue: 7,
                pr: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        WorkflowStatusHistoryResult actual = await WorkflowMcpTools.WorkflowStatusHistoryAsync(
            repo: REPOSITORY,
            reader: this._reader,
            issue: 7,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task WorkflowStatusHistoryFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in Failures())
        {
            this._reader.ReadAsync(
                    repository: REPOSITORY,
                    issue: null,
                    pr: 7,
                    cancellationToken: Arg.Any<CancellationToken>()
                )
                .ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(() =>
                WorkflowMcpTools.WorkflowStatusHistoryAsync(
                    repo: REPOSITORY,
                    reader: this._reader,
                    pr: 7,
                    cancellationToken: this.CancellationToken()
                )
            );

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task WorkflowStatusHistoryOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this._reader.ReadAsync(
                repository: REPOSITORY,
                issue: 7,
                pr: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            WorkflowMcpTools.WorkflowStatusHistoryAsync(
                repo: REPOSITORY,
                reader: this._reader,
                issue: 7,
                cancellationToken: this.CancellationToken()
            )
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> Failures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "pr", rule: "must be above 0"),
            new IssueNotFoundException("the issue " + REPOSITORY + "#7 was not found, or the caller cannot see it"),
            new PullRequestNotFoundException(
                "the pull request " + REPOSITORY + "#7 was not found, or the caller cannot see it"
            ),
            new WorkflowStatusHistoryFailedException(
                "could not read the Workflow status history of " + REPOSITORY + "#7"
            ),
            new WorkflowBoardNotFoundException("no Workflow board was found"),
            new WorkflowBoardAmbiguousException("more than one Workflow board was found"),
            new WorkflowBoardInvalidException("the Workflow board is not valid"),
        ];
    }
}
