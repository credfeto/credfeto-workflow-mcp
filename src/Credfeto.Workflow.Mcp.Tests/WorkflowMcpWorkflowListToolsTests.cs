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

public sealed class WorkflowMcpWorkflowListToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IWorkflowListService _service;

    public WorkflowMcpWorkflowListToolsTests()
    {
        this._service = GetSubstitute<IWorkflowListService>();
    }

    [Fact]
    public async Task WorkflowListReturnsWhatTheServiceListedAsync()
    {
        WorkflowListResponse expected = new(
            Repository: REPOSITORY,
            Workflows: [new WorkflowEntry(Name: "Build", Path: ".github/workflows/build.yml", State: "active")]
        );
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        WorkflowListResponse actual = await this.WorkflowListAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task WorkflowListFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in WorkflowListFailures())
        {
            this.WorkflowListFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.WorkflowListAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task WorkflowListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.WorkflowListFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.WorkflowListAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> WorkflowListFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new WorkflowListFailedException("could not list the workflows of " + REPOSITORY),
            new WorkflowListRefusedException(REPOSITORY + " has more than 1000 workflows to list"),
        ];
    }

    private void WorkflowListFailsWith(Exception failure)
    {
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<WorkflowListResponse> WorkflowListAsync()
    {
        return WorkflowMcpTools.WorkflowListAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
