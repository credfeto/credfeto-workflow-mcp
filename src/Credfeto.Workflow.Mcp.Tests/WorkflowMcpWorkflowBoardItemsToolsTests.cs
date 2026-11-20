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

public sealed class WorkflowMcpWorkflowBoardItemsToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly IWorkflowStatusCheckService _service;

    public WorkflowMcpWorkflowBoardItemsToolsTests()
    {
        this._service = GetSubstitute<IWorkflowStatusCheckService>();
    }

    [Fact]
    public async Task BoardItemsAreWhatTheServiceListedAsync()
    {
        WorkflowBoardItems expected = new(
            Repository: REPOSITORY,
            Total: 1,
            Items: [new(Kind: WorkflowBoardItemKind.Issue, Repository: REPOSITORY, Number: 1, State: "Approved")]
        );
        this.ListCallAsync().Returns(expected);

        WorkflowBoardItems actual = await this.BoardItemsAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task BoardItemsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in Failures())
        {
            this.ListCallAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.BoardItemsAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task BoardItemsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ListCallAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.BoardItemsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> Failures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "must be owner/name"),
            new WorkflowBoardNotFoundException("no project titled Workflow is linked to " + REPOSITORY),
            new WorkflowBoardAmbiguousException("more than one project titled Workflow is linked to " + REPOSITORY),
            new WorkflowBoardInvalidException("the Workflow board of " + REPOSITORY + " is invalid"),
            new BoardReadFailedException("could not list the items of project 7"),
        ];
    }

    private ValueTask<WorkflowBoardItems> ListCallAsync()
    {
        return this._service.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>());
    }

    private Task<WorkflowBoardItems> BoardItemsAsync()
    {
        return WorkflowMcpTools.WorkflowBoardItemsAsync(
            repo: REPOSITORY,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
