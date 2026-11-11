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

public sealed class WorkflowMcpRepoListToolsTests : TestBase
{
    private const string OWNER = "credfeto";

    private readonly IRepositoryListService _service;

    public WorkflowMcpRepoListToolsTests()
    {
        this._service = GetSubstitute<IRepositoryListService>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepoListReturnsWhatTheServiceListedAsync(bool includeArchived)
    {
        RepositoryListResponse expected = new(
            Owner: OWNER,
            Repositories:
            [
                new RepositoryEntry(
                    Repository: "credfeto/credfeto-workflow-mcp",
                    Visibility: "PUBLIC",
                    Archived: false,
                    DefaultBranch: "main"
                ),
            ]
        );
        this._service.ListAsync(
                owner: OWNER,
                includeArchived: includeArchived,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        RepositoryListResponse actual = await this.RepoListAsync(includeArchived);

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoListArchivedRepositoriesAreExcludedByDefaultAsync()
    {
        RepositoryListResponse expected = new(Owner: OWNER, Repositories: []);
        this._service.ListAsync(owner: OWNER, includeArchived: false, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        RepositoryListResponse actual = await WorkflowMcpTools.RepoListAsync(
            owner: OWNER,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoListFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoListFailures())
        {
            this.RepoListFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => this.RepoListAsync(false));

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.RepoListFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.RepoListAsync(false)
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoListFailures()
    {
        return
        [
            ParameterValidationException.For(
                parameterName: "owner",
                rule: "it must be letters, digits and hyphens, not starting with a hyphen"
            ),
            new OwnerNotFoundException("the owner credfeto was not found, or the caller cannot see it"),
            new RepositoryListFailedException("could not list the repositories of " + OWNER),
            new RepositoryListRefusedException(OWNER + " has more than 1000 repositories to list"),
        ];
    }

    private void RepoListFailsWith(Exception failure)
    {
        this._service.ListAsync(
                owner: OWNER,
                includeArchived: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<RepositoryListResponse> RepoListAsync(bool includeArchived)
    {
        return WorkflowMcpTools.RepoListAsync(
            owner: OWNER,
            service: this._service,
            includeArchived: includeArchived,
            cancellationToken: this.CancellationToken()
        );
    }
}
