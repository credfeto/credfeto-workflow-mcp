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

public sealed class WorkflowMcpSecurityAdvisoriesToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly ISecurityAdvisoriesService _service;

    public WorkflowMcpSecurityAdvisoriesToolsTests()
    {
        this._service = GetSubstitute<ISecurityAdvisoriesService>();
    }

    [Fact]
    public async Task RepoAdvisoriesReturnsWhatTheServiceListedAsync()
    {
        SecurityAdvisoriesResult expected = new(
            Repository: REPOSITORY,
            Advisories:
            [
                new SecurityAdvisoryEntry(
                    GhsaId: "GHSA-35jh-r3h4-6jhm",
                    Severity: "high",
                    State: "published",
                    Summary: "A summary"
                ),
            ]
        );
        this.ListReturnsAsync().Returns(expected);

        SecurityAdvisoriesResult actual = await this.RepoAdvisoriesAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoAdvisoriesFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoAdvisoriesFailures())
        {
            this.ListReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RepoAdvisoriesAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoAdvisoriesOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ListReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoAdvisoriesAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoAdvisoriesFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new SecurityAdvisoriesNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new SecurityAdvisoriesFailedException("could not list the security advisories of " + REPOSITORY),
        ];
    }

    private ValueTask<SecurityAdvisoriesResult> ListReturnsAsync()
    {
        return this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
    }

    private Task<SecurityAdvisoriesResult> RepoAdvisoriesAsync()
    {
        return WorkflowMcpTools.RepoAdvisoriesAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
