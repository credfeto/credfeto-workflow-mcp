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

public sealed class WorkflowMcpBranchCompareToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string BASE = "main";
    private const string HEAD = "feat/148-compare";

    private readonly IBranchCompareService _service;

    public WorkflowMcpBranchCompareToolsTests()
    {
        this._service = GetSubstitute<IBranchCompareService>();
    }

    [Fact]
    public async Task BranchCompareReturnsWhatTheServiceReadAsync()
    {
        BranchCompareResponse expected = new(
            Repo: REPOSITORY,
            Base: BASE,
            Head: HEAD,
            Status: BranchCompareStatus.Diverged,
            AheadBy: 1,
            BehindBy: 2
        );
        this.CompareReturnsAsync().Returns(expected);

        BranchCompareResponse actual = await this.BranchCompareAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task BranchCompareFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in BranchCompareFailures())
        {
            this.CompareReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.BranchCompareAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task BranchCompareOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.CompareReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.BranchCompareAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> BranchCompareFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "head", rule: "it must be a valid git ref name"),
            new BranchNotFoundException("the branch " + HEAD + " was not found in " + REPOSITORY),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new BranchCompareFailedException("could not compare main..." + HEAD + " in " + REPOSITORY),
        ];
    }

    private ValueTask<BranchCompareResponse> CompareReturnsAsync()
    {
        return this._service.CompareAsync(
            repository: REPOSITORY,
            baseBranch: BASE,
            headBranch: HEAD,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<BranchCompareResponse> BranchCompareAsync()
    {
        return WorkflowMcpTools.BranchCompareAsync(
            repo: REPOSITORY,
            @base: BASE,
            head: HEAD,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
