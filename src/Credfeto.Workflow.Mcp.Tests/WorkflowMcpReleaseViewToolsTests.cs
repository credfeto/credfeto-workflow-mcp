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

public sealed class WorkflowMcpReleaseViewToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IReleaseService _service;

    public WorkflowMcpReleaseViewToolsTests()
    {
        this._service = GetSubstitute<IReleaseService>();
    }

    [Fact]
    public async Task ReleaseViewReturnsWhatTheServiceReadAsync()
    {
        ReleaseViewResult expected = new(
            Tag: "v1.2.3",
            PublishedAt: "2026-11-17T09:39:14Z",
            Url: new("https://github.com/credfeto/credfeto-workflow-mcp/releases/tag/v1.2.3"),
            Name: "Release 1.2.3"
        );
        this.ViewReturnsAsync().Returns(expected);

        ReleaseViewResult actual = await this.ReleaseViewAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task ReleaseViewFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in ReleaseViewFailures())
        {
            this.ViewReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.ReleaseViewAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task ReleaseViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ViewReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.ReleaseViewAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> ReleaseViewFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be owner/name"),
            new ReleaseNotFoundException(REPOSITORY + " has no latest release"),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new ReleaseViewFailedException("could not read the latest release of " + REPOSITORY),
        ];
    }

    private ValueTask<ReleaseViewResult> ViewReturnsAsync()
    {
        return this._service.ViewLatestAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
    }

    private Task<ReleaseViewResult> ReleaseViewAsync()
    {
        return WorkflowMcpTools.ReleaseViewAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
