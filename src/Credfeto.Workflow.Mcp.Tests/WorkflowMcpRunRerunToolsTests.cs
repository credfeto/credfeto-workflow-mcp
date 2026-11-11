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

public sealed class WorkflowMcpRunRerunToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string RUN = "5";

    private readonly IRunRerunService _service;

    public WorkflowMcpRunRerunToolsTests()
    {
        this._service = GetSubstitute<IRunRerunService>();
    }

    [Theory]
    [InlineData(RunRerunOutcome.Success, "Rerunning failed jobs of run 5 (attempt 2)", 2, null)]
    [InlineData(RunRerunOutcome.Pending, "the run 5 has not completed, and can be rerun once it has", null, null)]
    [InlineData(RunRerunOutcome.Refused, "rerun refused: attempt limit", null, "attempt limit")]
    [InlineData(RunRerunOutcome.NotFound, "the run 5 was not found in " + REPOSITORY, null, null)]
    public async Task RunRerunReturnsWhatTheServiceDidAsync(
        RunRerunOutcome outcome,
        string message,
        int? attempt,
        string? rule
    )
    {
        RunRerunResult expected = new(Outcome: outcome, Message: message, Run: 5, Attempt: attempt, Rule: rule);
        this._service.RerunAsync(repository: REPOSITORY, run: RUN, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        RunRerunResult actual = await this.RunRerunAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RunRerunFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RunRerunFailures())
        {
            this.RunRerunFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RunRerunAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RunRerunOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.RunRerunFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RunRerunAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RunRerunFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "run", rule: "must be above 0"),
            new RunRerunFailedException("could not rerun the run 5 of " + REPOSITORY),
        ];
    }

    private void RunRerunFailsWith(Exception failure)
    {
        this._service.RerunAsync(repository: REPOSITORY, run: RUN, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<RunRerunResult> RunRerunAsync()
    {
        return WorkflowMcpTools.RunRerunAsync(
            repo: REPOSITORY,
            run: RUN,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
