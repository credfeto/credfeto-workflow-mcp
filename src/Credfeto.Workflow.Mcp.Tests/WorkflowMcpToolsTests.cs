using System;
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

public sealed class WorkflowMcpToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;

    private readonly IPullRequestCommentsReader _reader;

    public WorkflowMcpToolsTests()
    {
        this._reader = GetSubstitute<IPullRequestCommentsReader>();
    }

    [Fact]
    public void PingReturnsPong()
    {
        PingResponse response = WorkflowMcpTools.Ping();

        Assert.Equal(expected: "pong", actual: response.Value);
    }

    [Fact]
    public async Task PrCommentsReturnsWhatTheReaderReadAsync()
    {
        PullRequestComments expected = new(
            Pr: REPOSITORY + "#41",
            ReviewDecision: "APPROVED",
            Comments: [],
            Reviews: [],
            Threads: [],
            Summary: new(Comments: 0, Reviews: 0, ReviewsEmpty: 0, Threads: 0, Unresolved: 0, Inline: 0)
        );
        this._reader.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PullRequestComments actual = await this.ReadAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task PullRequestNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertReportedAsync(
            new PullRequestNotFoundException("pull request " + REPOSITORY + "#41 was not found")
        );
    }

    [Fact]
    public Task IncompleteCommentsAreReportedToTheCallerAsync()
    {
        return this.AssertReportedAsync(
            new PullRequestCommentsIncompleteException("could not read all the comments of " + REPOSITORY + "#41")
        );
    }

    [Fact]
    public Task InvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertReportedAsync(
            ParameterValidationException.For(parameterName: "repo", rule: "must be owner/name")
        );
    }

    [Fact]
    public async Task OtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubGraphQlException failure = new("upstream detail");
        this.ReaderFailsWith(failure);

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(this.ReadAsync);

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task AssertReportedAsync(Exception failure)
    {
        this.ReaderFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.ReadAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void ReaderFailsWith(Exception failure)
    {
        this._reader.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PullRequestComments> ReadAsync()
    {
        return WorkflowMcpTools.PrCommentsAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            reader: this._reader,
            cancellationToken: this.CancellationToken()
        );
    }
}
