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

public sealed class WorkflowMcpLabelListToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly ILabelListService _service;

    public WorkflowMcpLabelListToolsTests()
    {
        this._service = GetSubstitute<ILabelListService>();
    }

    [Fact]
    public async Task LabelListReturnsWhatTheServiceListedAsync()
    {
        LabelListResponse expected = new(Repository: REPOSITORY, Labels: ["bug", "Priority 1"]);
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(expected);

        LabelListResponse actual = await this.LabelListAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task LabelListFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in LabelListFailures())
        {
            this.LabelListFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.LabelListAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task LabelListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.LabelListFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.LabelListAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> LabelListFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
            new LabelListFailedException("could not list the labels of " + REPOSITORY),
            new LabelListFailedException(REPOSITORY + " has more than 1000 labels to list"),
        ];
    }

    private void LabelListFailsWith(Exception failure)
    {
        this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
    }

    private Task<LabelListResponse> LabelListAsync()
    {
        return WorkflowMcpTools.LabelListAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
