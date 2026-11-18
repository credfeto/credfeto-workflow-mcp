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

public sealed class WorkflowMcpRepoViewToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IRepoViewService _service;

    public WorkflowMcpRepoViewToolsTests()
    {
        this._service = GetSubstitute<IRepoViewService>();
    }

    [Fact]
    public async Task RepoViewReturnsWhatTheServiceReadAsync()
    {
        RepoViewResult expected = new(
            DefaultBranch: "main",
            Visibility: "PUBLIC",
            AllowMergeCommit: true,
            AllowSquashMerge: null,
            AllowRebaseMerge: false,
            Description: "A workflow MCP server"
        );
        this._service.ViewAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        RepoViewResult actual = await this.RepoViewAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoViewFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoViewFailures())
        {
            this.RepoViewFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RepoViewAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.RepoViewFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoViewAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoViewFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new RepoViewFailedException("could not view the repository " + REPOSITORY),
        ];
    }

    private void RepoViewFailsWith(Exception failure)
    {
        this._service.ViewAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<RepoViewResult> RepoViewAsync()
    {
        return WorkflowMcpTools.RepoViewAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
