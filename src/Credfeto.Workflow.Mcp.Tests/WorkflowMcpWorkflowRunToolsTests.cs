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

public sealed class WorkflowMcpWorkflowRunToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string WORKFLOW = "deploy.yml";
    private const string REF = "release/1";

    private static readonly IReadOnlyList<string> Inputs = ["environment=staging"];

    private readonly IWorkflowRunService _service;

    public WorkflowMcpWorkflowRunToolsTests()
    {
        this._service = GetSubstitute<IWorkflowRunService>();
    }

    [Theory]
    [InlineData(WorkflowRunOutcome.Success, "dispatched; newest run on release/1 is 5", 5L, null)]
    [InlineData(WorkflowRunOutcome.Refused, "workflow run refused: no allowlist", null, "no allowlist")]
    [InlineData(WorkflowRunOutcome.NotFound, "the workflow deploy.yml was not found", null, null)]
    [InlineData(WorkflowRunOutcome.InvalidInput, "the workflow does not declare the input environment", null, null)]
    public async Task WorkflowRunReturnsWhatTheServiceDidAsync(
        WorkflowRunOutcome outcome,
        string message,
        long? run,
        string? rule
    )
    {
        WorkflowRunResult expected = new(
            Outcome: outcome,
            Message: message,
            Workflow: WORKFLOW,
            Ref: REF,
            Run: run,
            Url: null,
            Rule: rule
        );
        this.ServiceCallAsync().Returns(expected);

        WorkflowRunResult actual = await this.WorkflowRunAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task WorkflowRunFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in WorkflowRunFailures())
        {
            this.ServiceCallAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.WorkflowRunAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task WorkflowRunOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ServiceCallAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.WorkflowRunAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> WorkflowRunFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "workflow", rule: "it must be the file name of a workflow"),
            new WorkflowRunFailedException("could not run the workflow deploy.yml of " + REPOSITORY),
        ];
    }

    private ValueTask<WorkflowRunResult> ServiceCallAsync()
    {
        return this._service.RunAsync(
            repository: REPOSITORY,
            workflow: WORKFLOW,
            gitRef: REF,
            inputs: Inputs,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<WorkflowRunResult> WorkflowRunAsync()
    {
        return WorkflowMcpTools.WorkflowRunAsync(
            repo: REPOSITORY,
            workflow: WORKFLOW,
            service: this._service,
            @ref: REF,
            inputs: Inputs,
            cancellationToken: this.CancellationToken()
        );
    }
}
