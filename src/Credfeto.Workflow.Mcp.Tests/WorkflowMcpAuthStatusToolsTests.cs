using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpAuthStatusToolsTests : TestBase
{
    private readonly IAuthStatusService _service;

    public WorkflowMcpAuthStatusToolsTests()
    {
        this._service = GetSubstitute<IAuthStatusService>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthStatusReturnsWhatTheServiceReadAsync(bool rateLimit)
    {
        AuthStatusResponse expected = new(
            Login: "credfeto",
            Host: "mcp.example",
            Version: "2026-03-10",
            RateLimits: null
        );
        this._service.ReadAsync(rateLimit: rateLimit, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        AuthStatusResponse actual = await this.AuthStatusAsync(rateLimit);

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task AuthStatusDoesNotReadTheRateLimitsUnlessAskedAsync()
    {
        AuthStatusResponse expected = new(
            Login: "credfeto",
            Host: string.Empty,
            Version: string.Empty,
            RateLimits: null
        );
        this._service.ReadAsync(rateLimit: false, cancellationToken: Arg.Any<CancellationToken>()).Returns(expected);

        AuthStatusResponse actual = await WorkflowMcpTools.AuthStatusAsync(
            service: this._service,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task AuthStatusFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in AuthStatusFailures())
        {
            this.AuthStatusFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(() => this.AuthStatusAsync(true));

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task AuthStatusOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.AuthStatusFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(() =>
            this.AuthStatusAsync(true)
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> AuthStatusFailures()
    {
        return
        [
            new NotAuthenticatedException("not authenticated"),
            new AuthStatusFailedException("could not read the authenticated user: GitHub answered with status 500"),
            new AuthStatusFailedException("unexpected result for the rate limit"),
        ];
    }

    private void AuthStatusFailsWith(Exception failure)
    {
        this._service.ReadAsync(rateLimit: Arg.Any<bool>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<AuthStatusResponse> AuthStatusAsync(bool rateLimit)
    {
        return WorkflowMcpTools.AuthStatusAsync(
            service: this._service,
            rateLimit: rateLimit,
            cancellationToken: this.CancellationToken()
        );
    }
}
