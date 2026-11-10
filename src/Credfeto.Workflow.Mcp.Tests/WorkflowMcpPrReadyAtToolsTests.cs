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

public sealed class WorkflowMcpPrReadyAtToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 51;
    private const string REFERENCE = REPOSITORY + "#51";

    private readonly IPrReadyAtService _service;

    public WorkflowMcpPrReadyAtToolsTests()
    {
        this._service = GetSubstitute<IPrReadyAtService>();
    }

    [Theory]
    [InlineData("2026-01-02T03:04:05Z", "ready-for-review")]
    [InlineData("2026-01-01T00:00:00Z", "created")]
    public async Task PrReadyAtReturnsWhatTheServiceReadAsync(string readyAt, string source)
    {
        PrReadyAtResponse expected = new(ReadyAt: readyAt, Source: source);
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrReadyAtResponse actual = await this.PrReadyAtAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrReadyAtFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in PrReadyAtFailures())
        {
            this.PrReadyAtFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.PrReadyAtAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrReadyAtOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrReadyAtFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrReadyAtAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> PrReadyAtFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "pullRequest", rule: "must be above 0"),
            new PullRequestNotFoundException("pull request " + REFERENCE + " was not found"),
            new PrReadyAtFailedException("could not read when pull request " + REFERENCE + " became ready for review"),
        ];
    }

    private void PrReadyAtFailsWith(Exception failure)
    {
        this._service.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrReadyAtResponse> PrReadyAtAsync()
    {
        return WorkflowMcpTools.PrReadyAtAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
