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

public sealed class WorkflowMcpReleaseListToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IReleaseService _service;

    public WorkflowMcpReleaseListToolsTests()
    {
        this._service = GetSubstitute<IReleaseService>();
    }

    [Fact]
    public async Task ReleaseListReturnsWhatTheServiceListedAsync()
    {
        ReleaseListResponse expected = new(
            Repository: REPOSITORY,
            Releases:
            [
                new(
                    Tag: "v1.2.3",
                    PublishedAt: "2026-11-17T09:39:14Z",
                    IsLatest: true,
                    IsPrerelease: false,
                    Name: "Release 1.2.3"
                ),
            ]
        );
        this.ListReturnsAsync().Returns(expected);

        ReleaseListResponse actual = await this.ReleaseListAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task ReleaseListFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in ReleaseListFailures())
        {
            this.ListReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.ReleaseListAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task ReleaseListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ListReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.ReleaseListAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> ReleaseListFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be owner/name"),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new ReleaseListFailedException("could not list the releases of " + REPOSITORY),
            new ReleaseListRefusedException(REPOSITORY + " has more than 1000 releases, so none is printed"),
        ];
    }

    private ValueTask<ReleaseListResponse> ListReturnsAsync()
    {
        return this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
    }

    private Task<ReleaseListResponse> ReleaseListAsync()
    {
        return WorkflowMcpTools.ReleaseListAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
