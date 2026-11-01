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

public sealed class WorkflowMcpToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;

    private const string BRANCH = "main";

    private readonly IRequiredChecksReader _checksReader;
    private readonly IPullRequestCommentsReader _reader;
    private readonly IPullRequestService _viewService;

    public WorkflowMcpToolsTests()
    {
        this._reader = GetSubstitute<IPullRequestCommentsReader>();
        this._checksReader = GetSubstitute<IRequiredChecksReader>();
        this._viewService = GetSubstitute<IPullRequestService>();
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

    [Fact]
    public async Task BranchRequiredChecksReturnsWhatTheReaderReadAsync()
    {
        BranchRequiredChecksResponse expected = new(Repo: REPOSITORY, Branch: BRANCH, Checks: ["build", "lint"]);
        this._checksReader.ReadRequiredCheckNamesAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        BranchRequiredChecksResponse actual = await this.ReadRequiredChecksAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task BranchNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertChecksFailureReportedAsync(
            new BranchNotFoundException("the branch " + BRANCH + " was not found in " + REPOSITORY)
        );
    }

    [Fact]
    public Task RepositoryNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertChecksFailureReportedAsync(
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            )
        );
    }

    [Fact]
    public Task UnreadableRequiredChecksAreReportedToTheCallerAsync()
    {
        return this.AssertChecksFailureReportedAsync(
            new RequiredChecksUnreadableException(
                "could not read the required checks of " + REPOSITORY + " branch " + BRANCH
            )
        );
    }

    [Fact]
    public Task InvalidBranchParameterIsReportedToTheCallerAsync()
    {
        return this.AssertChecksFailureReportedAsync(
            ParameterValidationException.For(parameterName: "branch", rule: "must not start with '-'")
        );
    }

    [Fact]
    public async Task OtherRequiredChecksFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ChecksReaderFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.ReadRequiredChecksAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task PrViewReturnsWhatTheServiceReadAsync()
    {
        IReadOnlyList<string> fields = [PullRequestViewFields.Title, PullRequestViewFields.State];
        PullRequestView expected = CreateView(title: "Add the pr_view tool", state: "OPEN");
        this._viewService.ViewAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                fields: fields,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PullRequestView actual = await this.ViewAsync(fields);

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrViewPassesNoFieldsWhenNoneAreGivenAsync()
    {
        PullRequestView expected = CreateView(title: "Add the pr_view tool", state: "OPEN");
        this._viewService.ViewAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                fields: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PullRequestView actual = await WorkflowMcpTools.PrViewAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._viewService,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task PrViewPullRequestNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertViewFailureReportedAsync(
            new PullRequestNotFoundException("pull request " + REPOSITORY + "#41 was not found")
        );
    }

    [Fact]
    public Task PrViewIncompleteReadIsReportedToTheCallerAsync()
    {
        return this.AssertViewFailureReportedAsync(
            new PullRequestViewFailedException("could not read all of " + REPOSITORY + "#41")
        );
    }

    [Fact]
    public Task PrViewInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertViewFailureReportedAsync(
            ParameterValidationException.For(parameterName: "fields", rule: "must only contain known field names")
        );
    }

    [Fact]
    public async Task PrViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubGraphQlException failure = new("upstream detail");
        this.ViewServiceFailsWith(failure);

        GitHubGraphQlException exception = await Assert.ThrowsAsync<GitHubGraphQlException>(() => this.ViewAsync(null));

        Assert.Same(expected: failure, actual: exception);
    }

    private static PullRequestView CreateView(string title, string state)
    {
        return new(
            Pr: REPOSITORY + "#41",
            State: state,
            Draft: null,
            Title: title,
            Url: null,
            Base: null,
            Head: null,
            HeadSha: null,
            AutoMerge: null,
            ReviewDecision: null,
            MergeState: null,
            Mergeable: null,
            CreatedAt: null,
            MergedAt: null,
            MergeCommit: null,
            Labels: null,
            Assignees: null,
            Author: null,
            CommitAuthors: null,
            Files: null,
            Body: null
        );
    }

    private async Task AssertViewFailureReportedAsync(Exception failure)
    {
        this.ViewServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(() => this.ViewAsync(null));

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void ViewServiceFailsWith(Exception failure)
    {
        this._viewService.ViewAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                fields: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PullRequestView> ViewAsync(IReadOnlyList<string>? fields)
    {
        return WorkflowMcpTools.PrViewAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._viewService,
            cancellationToken: this.CancellationToken(),
            fields: fields
        );
    }

    private async Task AssertChecksFailureReportedAsync(Exception failure)
    {
        this.ChecksReaderFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.ReadRequiredChecksAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void ChecksReaderFailsWith(Exception failure)
    {
        this._checksReader.ReadRequiredCheckNamesAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<BranchRequiredChecksResponse> ReadRequiredChecksAsync()
    {
        return WorkflowMcpTools.BranchRequiredChecksAsync(
            repo: REPOSITORY,
            branch: BRANCH,
            reader: this._checksReader,
            cancellationToken: this.CancellationToken()
        );
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
