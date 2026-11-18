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

public sealed class WorkflowMcpRepoCollaboratorsToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IRepoCollaboratorsService _service;

    public WorkflowMcpRepoCollaboratorsToolsTests()
    {
        this._service = GetSubstitute<IRepoCollaboratorsService>();
    }

    [Fact]
    public async Task RepoCollaboratorsReturnsWhatTheServiceListedAsync()
    {
        RepoCollaboratorsResponse expected = new(
            Repository: REPOSITORY,
            Collaborators: [new RepoCollaborator(Login: "credfeto", Role: "admin")]
        );
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        RepoCollaboratorsResponse actual = await this.RepoCollaboratorsAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoCollaboratorsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoCollaboratorsFailures())
        {
            this.RepoCollaboratorsFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RepoCollaboratorsAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoCollaboratorsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.RepoCollaboratorsFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoCollaboratorsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoCollaboratorsFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new RepoCollaboratorsFailedException("could not list the collaborators of " + REPOSITORY),
            new RepoCollaboratorsFailedException(
                "the caller lacks push access to " + REPOSITORY + ", which GitHub requires to list its collaborators"
            ),
            new RepoCollaboratorsFailedException(REPOSITORY + " has more than 1000 collaborators, so none is listed"),
        ];
    }

    private void RepoCollaboratorsFailsWith(Exception failure)
    {
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<RepoCollaboratorsResponse> RepoCollaboratorsAsync()
    {
        return WorkflowMcpTools.RepoCollaboratorsAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
