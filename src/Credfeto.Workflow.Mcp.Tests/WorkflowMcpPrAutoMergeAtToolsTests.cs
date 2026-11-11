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

public sealed class WorkflowMcpPrAutoMergeAtToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 62;
    private const string REFERENCE = REPOSITORY + "#62";

    private readonly IPrAutoMergeAtService _service;

    public WorkflowMcpPrAutoMergeAtToolsTests()
    {
        this._service = GetSubstitute<IPrAutoMergeAtService>();
    }

    [Theory]
    [InlineData("2026-01-02T03:04:05Z")]
    [InlineData("-")]
    public async Task PrAutoMergeAtReturnsWhatTheServiceReadAsync(string enabledAt)
    {
        PrAutoMergeAtResponse expected = new(Pr: PULL_REQUEST, EnabledAt: enabledAt);
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrAutoMergeAtResponse actual = await this.PrAutoMergeAtAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrAutoMergeAtFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in PrAutoMergeAtFailures())
        {
            this.PrAutoMergeAtFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.PrAutoMergeAtAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrAutoMergeAtOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrAutoMergeAtFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrAutoMergeAtAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> PrAutoMergeAtFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "pullRequest", rule: "must be above 0"),
            new PullRequestNotFoundException("pull request " + REFERENCE + " was not found"),
            new PrAutoMergeAtFailedException("could not read when auto-merge was enabled on pull request " + REFERENCE),
        ];
    }

    private void PrAutoMergeAtFailsWith(Exception failure)
    {
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrAutoMergeAtResponse> PrAutoMergeAtAsync()
    {
        return WorkflowMcpTools.PrAutoMergeAtAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
