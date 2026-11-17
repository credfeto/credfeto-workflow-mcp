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

public sealed class WorkflowMcpTagViewToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string TAG = "v1.0.0";

    private readonly ITagViewService _service;

    public WorkflowMcpTagViewToolsTests()
    {
        this._service = GetSubstitute<ITagViewService>();
    }

    [Fact]
    public async Task TagViewReturnsWhatTheServiceReadAsync()
    {
        TagViewResponse expected = new(Sha: "0123456789abcdef0123456789abcdef01234567", Type: TagType.Annotated);
        this.ViewReturnsAsync().Returns(expected);

        TagViewResponse actual = await this.TagViewAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task TagViewFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in TagViewFailures())
        {
            this.ViewReturnsAsync().ThrowsAsync(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.TagViewAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task TagViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ViewReturnsAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.TagViewAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> TagViewFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "tag", rule: "it must be a valid git ref name"),
            new TagNotFoundException("the tag v1.0.0 was not found in " + REPOSITORY),
            new RepositoryNotFoundException("the repository " + REPOSITORY + " was not found"),
            new TagViewFailedException("could not read the tag v1.0.0 in " + REPOSITORY),
        ];
    }

    private ValueTask<TagViewResponse> ViewReturnsAsync()
    {
        return this._service.ViewAsync(
            repository: REPOSITORY,
            tag: TAG,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<TagViewResponse> TagViewAsync()
    {
        return WorkflowMcpTools.TagViewAsync(
            repo: REPOSITORY,
            tag: TAG,
            service: this._service,
            cancellationToken: this.CancellationToken()
        );
    }
}
