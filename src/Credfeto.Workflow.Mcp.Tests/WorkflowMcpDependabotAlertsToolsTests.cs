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

public sealed class WorkflowMcpDependabotAlertsToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string STATE = "fixed";
    private const string SEVERITY = "critical";
    private const int LIMIT = 5;

    private readonly IDependabotAlertsService _service;

    public WorkflowMcpDependabotAlertsToolsTests()
    {
        this._service = GetSubstitute<IDependabotAlertsService>();
    }

    [Fact]
    public async Task RepoDependabotAlertsReturnsWhatTheServiceListedAsync()
    {
        DependabotAlertsResult expected = new(
            Repository: REPOSITORY,
            Alerts:
            [
                new DependabotAlertEntry(
                    Number: 3,
                    Severity: SEVERITY,
                    State: STATE,
                    Ecosystem: "npm",
                    Package: "lodash",
                    ManifestPath: "package-lock.json",
                    VulnerableRange: "= 4.17.20",
                    FirstPatchedVersion: "4.17.21",
                    GhsaId: "GHSA-35jh-r3h4-6jhm"
                ),
            ]
        );
        this.ListReturnsAsync().Returns(expected);

        DependabotAlertsResult actual = await this.RepoDependabotAlertsAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoDependabotAlertsDefaultsAreLeftToTheServiceAsync()
    {
        DependabotAlertsResult expected = new(Repository: REPOSITORY, Alerts: []);
        this._service.ListAsync(
                repository: REPOSITORY,
                state: null,
                severity: null,
                limit: 30,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        DependabotAlertsResult actual = await WorkflowMcpTools.RepoDependabotAlertsAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoDependabotAlertsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in RepoDependabotAlertsFailures())
        {
            this.ListReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.RepoDependabotAlertsAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task RepoDependabotAlertsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ListReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoDependabotAlertsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> RepoDependabotAlertsFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new DependabotAlertsNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new DependabotAlertsDisabledException("Dependabot alerts are disabled for " + REPOSITORY),
            new DependabotAlertsPermissionException(
                "the caller's token is not allowed to read the Dependabot alerts of " + REPOSITORY
            ),
            new DependabotAlertsFailedException("could not list the Dependabot alerts of " + REPOSITORY),
        ];
    }

    private ValueTask<DependabotAlertsResult> ListReturnsAsync()
    {
        return this._service.ListAsync(
            repository: REPOSITORY,
            state: STATE,
            severity: SEVERITY,
            limit: LIMIT,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<DependabotAlertsResult> RepoDependabotAlertsAsync()
    {
        return WorkflowMcpTools.RepoDependabotAlertsAsync(
            repo: REPOSITORY,
            service: this._service,
            state: STATE,
            severity: SEVERITY,
            limit: LIMIT,
            cancellationToken: this.CancellationToken()
        );
    }
}
