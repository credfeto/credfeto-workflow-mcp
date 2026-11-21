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

public sealed class WorkflowMcpLabelHistoryToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly ILabelHistoryReader _reader;

    public WorkflowMcpLabelHistoryToolsTests()
    {
        this._reader = GetSubstitute<ILabelHistoryReader>();
    }

    [Fact]
    public async Task LabelHistoryReturnsWhatTheReaderReadAsync()
    {
        LabelHistoryResult expected = new(
            Repository: REPOSITORY,
            Number: 7,
            Changes: [new(Action: "added", Time: "2026-01-02T03:04:05Z", Actor: "credfeto", Label: "bug")]
        );
        this._reader.ReadAsync(
                repository: REPOSITORY,
                issue: 7,
                pr: null,
                label: "bug",
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelHistoryResult actual = await WorkflowMcpTools.LabelHistoryAsync(
            repo: REPOSITORY,
            reader: this._reader,
            issue: 7,
            label: "bug",
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task LabelHistoryFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in Failures())
        {
            this._reader.ReadAsync(
                    repository: REPOSITORY,
                    issue: null,
                    pr: 7,
                    label: null,
                    cancellationToken: Arg.Any<CancellationToken>()
                )
                .ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(() =>
                WorkflowMcpTools.LabelHistoryAsync(
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
    public async Task LabelHistoryOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this._reader.ReadAsync(
                repository: REPOSITORY,
                issue: 7,
                pr: null,
                label: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            WorkflowMcpTools.LabelHistoryAsync(
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
            new LabelHistoryFailedException("could not read the label history of " + REPOSITORY + "#7"),
        ];
    }
}
