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

public sealed class WorkflowMcpAdvisoryViewToolsTests : TestBase
{
    private const string GHSA = "GHSA-jfh8-c2jp-5v3q";

    private readonly IAdvisoryViewService _service;

    public WorkflowMcpAdvisoryViewToolsTests()
    {
        this._service = GetSubstitute<IAdvisoryViewService>();
    }

    [Fact]
    public async Task AdvisoryViewReturnsWhatTheServiceReadAsync()
    {
        AdvisoryViewResult expected = new(
            GhsaId: GHSA,
            Severity: "critical",
            Summary: "Remote code injection",
            Published: "2021-12-10T00:40:56Z",
            Withdrawn: string.Empty,
            Packages:
            [
                new(Ecosystem: "nuget", Package: "Example.Package", VulnerableRange: "< 1.2.3", FirstPatched: "1.2.3"),
            ]
        );
        this.ViewReturnsAsync().Returns(expected);

        AdvisoryViewResult actual = await this.AdvisoryViewAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task AdvisoryViewFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in AdvisoryViewFailures())
        {
            this.ViewReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.AdvisoryViewAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task AdvisoryViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ViewReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.AdvisoryViewAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> AdvisoryViewFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "ghsa", rule: "it must be a GHSA id"),
            new AdvisoryNotFoundException("the security advisory " + GHSA + " was not found"),
            new AdvisoryViewFailedException("could not read the security advisory " + GHSA),
        ];
    }

    private ValueTask<AdvisoryViewResult> ViewReturnsAsync()
    {
        return this._service.ViewAsync(ghsa: GHSA, cancellationToken: Arg.Any<CancellationToken>());
    }

    private Task<AdvisoryViewResult> AdvisoryViewAsync()
    {
        return WorkflowMcpTools.AdvisoryViewAsync(
            ghsa: GHSA,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
