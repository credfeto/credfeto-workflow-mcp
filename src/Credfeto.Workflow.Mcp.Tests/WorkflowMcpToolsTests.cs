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

    private const int ISSUE = 16;

    private const string BRANCH = "main";

    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string STATUS = "Development";

    private readonly IRequiredChecksReader _checksReader;
    private readonly IIssueService _issueService;
    private readonly IPrChecksService _prChecksService;
    private readonly IPullRequestCommentsReader _reader;
    private readonly IPullRequestService _viewService;
    private readonly IWorkflowStatusCheckService _workflowStatusCheckService;
    private readonly IWorkflowStatusSetService _workflowStatusSetService;

    public WorkflowMcpToolsTests()
    {
        this._workflowStatusCheckService = GetSubstitute<IWorkflowStatusCheckService>();
        this._workflowStatusSetService = GetSubstitute<IWorkflowStatusSetService>();
        this._reader = GetSubstitute<IPullRequestCommentsReader>();
        this._checksReader = GetSubstitute<IRequiredChecksReader>();
        this._viewService = GetSubstitute<IPullRequestService>();
        this._issueService = GetSubstitute<IIssueService>();
        this._prChecksService = GetSubstitute<IPrChecksService>();
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

    [Fact]
    public async Task IssueViewReturnsWhatTheServiceReadAsync()
    {
        IssueViewResponse expected = new(
            Issue: REPOSITORY + "#16",
            Title: "Add the issue_view tool",
            State: "OPEN",
            Labels: ["feature"],
            Assignees: ["credfeto"],
            Body: "Body text"
        );
        this._issueService.ViewAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueViewResponse actual = await this.IssueViewAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task IssueViewIssueNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertIssueViewFailureReportedAsync(
            new IssueNotFoundException("issue " + REPOSITORY + "#16 was not found")
        );
    }

    [Fact]
    public Task IssueViewFailedReadIsReportedToTheCallerAsync()
    {
        return this.AssertIssueViewFailureReportedAsync(
            new IssueViewFailedException("could not read issue " + REPOSITORY + "#16")
        );
    }

    [Fact]
    public Task IssueViewInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertIssueViewFailureReportedAsync(
            ParameterValidationException.For(parameterName: "issue", rule: "must be above 0")
        );
    }

    [Fact]
    public async Task IssueViewOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueServiceFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueViewAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task PrChecksReturnsWhatTheServiceReadAsync()
    {
        PrChecksResponse expected = new(
            Repo: REPOSITORY,
            Sha: SHA,
            Verdict: PrChecksVerdict.Passed,
            Passed: 1,
            Failed: 0,
            Pending: 0,
            Checks: []
        );
        this._prChecksService.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrChecksResponse actual = await this.PrChecksAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrChecksPassesShaBranchAndAllThroughAsync()
    {
        PrChecksResponse expected = new(
            Repo: REPOSITORY,
            Sha: SHA,
            Verdict: PrChecksVerdict.Pending,
            Passed: 0,
            Failed: 0,
            Pending: 1,
            Checks: []
        );
        this._prChecksService.ReadAsync(
                repository: REPOSITORY,
                pullRequest: null,
                sha: SHA,
                branch: BRANCH,
                all: true,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrChecksResponse actual = await WorkflowMcpTools.PrChecksAsync(
            repo: REPOSITORY,
            service: this._prChecksService,
            sha: SHA,
            branch: BRANCH,
            all: true,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task PrChecksPullRequestNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new PullRequestNotFoundException("pull request " + REPOSITORY + "#41 was not found")
        );
    }

    [Fact]
    public Task PrChecksCommitNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new CommitNotFoundException("the commit " + SHA + " was not found in " + REPOSITORY)
        );
    }

    [Fact]
    public Task PrChecksBranchNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new BranchNotFoundException("the branch " + BRANCH + " was not found in " + REPOSITORY)
        );
    }

    [Fact]
    public Task PrChecksRepositoryNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            )
        );
    }

    [Fact]
    public Task PrChecksUnreadableRequiredChecksAreReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new RequiredChecksUnreadableException(
                "could not read the required checks of " + REPOSITORY + " branch " + BRANCH
            )
        );
    }

    [Fact]
    public Task PrChecksUnreadableChecksAreReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            new PrChecksUnreadableException("could not read the checks of " + REPOSITORY + " commit " + SHA)
        );
    }

    [Fact]
    public Task PrChecksInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertPrChecksFailureReportedAsync(
            ParameterValidationException.For(parameterName: "pr", rule: "give exactly one of pr, sha or branch")
        );
    }

    [Fact]
    public async Task PrChecksOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrChecksServiceFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrChecksAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task WorkflowStatusCheckReturnsWhatTheServiceReadForAPullRequestAsync()
    {
        WorkflowStatusCheckResult expected = new(
            Outcome: WorkflowStatusCheckOutcome.Found,
            State: "Development",
            Message: null
        );
        this._workflowStatusCheckService.CheckAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        WorkflowStatusCheckResult actual = await this.WorkflowStatusCheckAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task WorkflowStatusCheckPassesTheIssueThroughAsync()
    {
        WorkflowStatusCheckResult expected = new(Outcome: WorkflowStatusCheckOutcome.Unset, State: null, Message: null);
        this._workflowStatusCheckService.CheckAsync(
                repository: REPOSITORY,
                pullRequest: null,
                issue: ISSUE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        WorkflowStatusCheckResult actual = await WorkflowMcpTools.WorkflowStatusCheckAsync(
            repo: REPOSITORY,
            service: this._workflowStatusCheckService,
            issue: ISSUE,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task WorkflowStatusCheckBoardNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusCheckFailureReportedAsync(
            new WorkflowBoardNotFoundException("no Workflow board is linked to " + REPOSITORY)
        );
    }

    [Fact]
    public Task WorkflowStatusCheckAmbiguousBoardIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusCheckFailureReportedAsync(
            new WorkflowBoardAmbiguousException("more than one Workflow board is linked to " + REPOSITORY)
        );
    }

    [Fact]
    public Task WorkflowStatusCheckInvalidBoardIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusCheckFailureReportedAsync(
            new WorkflowBoardInvalidException("the Workflow board linked to " + REPOSITORY + " is invalid")
        );
    }

    [Fact]
    public Task WorkflowStatusCheckBoardReadFailureIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusCheckFailureReportedAsync(
            new BoardReadFailedException("could not read the Workflow board status of " + REPOSITORY + "#41")
        );
    }

    [Fact]
    public Task WorkflowStatusCheckInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusCheckFailureReportedAsync(
            ParameterValidationException.For(parameterName: "pr", rule: "exactly one of pr or issue must be given")
        );
    }

    [Fact]
    public async Task WorkflowStatusCheckOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.WorkflowStatusCheckServiceFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.WorkflowStatusCheckAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task AssertWorkflowStatusCheckFailureReportedAsync(Exception failure)
    {
        this.WorkflowStatusCheckServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.WorkflowStatusCheckAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void WorkflowStatusCheckServiceFailsWith(Exception failure)
    {
        this._workflowStatusCheckService.CheckAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<WorkflowStatusCheckResult> WorkflowStatusCheckAsync()
    {
        return WorkflowMcpTools.WorkflowStatusCheckAsync(
            repo: REPOSITORY,
            service: this._workflowStatusCheckService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task WorkflowStatusSetReturnsWhatTheServiceSetForAPullRequestAsync()
    {
        WorkflowStatusSetResponse expected = new(
            ItemUrl: new(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/pull/41",
                uriKind: UriKind.Absolute
            ),
            Status: STATUS
        );
        this._workflowStatusSetService.SetAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                status: STATUS,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        WorkflowStatusSetResponse actual = await this.WorkflowStatusSetAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task WorkflowStatusSetPassesTheIssueThroughAsync()
    {
        WorkflowStatusSetResponse expected = new(
            ItemUrl: new(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/16",
                uriKind: UriKind.Absolute
            ),
            Status: STATUS
        );
        this._workflowStatusSetService.SetAsync(
                repository: REPOSITORY,
                pullRequest: null,
                issue: ISSUE,
                status: STATUS,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        WorkflowStatusSetResponse actual = await WorkflowMcpTools.WorkflowStatusSetAsync(
            repo: REPOSITORY,
            status: STATUS,
            service: this._workflowStatusSetService,
            issue: ISSUE,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task WorkflowStatusSetInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            ParameterValidationException.For(parameterName: "pr", rule: "exactly one of pr or issue must be given")
        );
    }

    [Fact]
    public Task WorkflowStatusSetUnknownStateIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new UnknownWorkflowStateException("unknown --status (valid: Development)")
        );
    }

    [Fact]
    public Task WorkflowStatusSetBoardNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new WorkflowBoardNotFoundException("no Workflow board is linked to " + REPOSITORY)
        );
    }

    [Fact]
    public Task WorkflowStatusSetAmbiguousBoardIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new WorkflowBoardAmbiguousException("more than one Workflow board is linked to " + REPOSITORY)
        );
    }

    [Fact]
    public Task WorkflowStatusSetInvalidBoardIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new WorkflowBoardInvalidException("the Workflow board linked to " + REPOSITORY + " is invalid")
        );
    }

    [Fact]
    public Task WorkflowStatusSetMissingStatusFieldIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new WorkflowStatusFieldNotFoundException("the Workflow board has no Status field")
        );
    }

    [Fact]
    public Task WorkflowStatusSetStatusFieldReadFailureIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new StatusFieldReadFailedException("could not read the Workflow board Status field")
        );
    }

    [Fact]
    public Task WorkflowStatusSetMissingStatusOptionIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new StatusOptionMissingException("the Workflow board has no Development option")
        );
    }

    [Fact]
    public Task WorkflowStatusSetAmbiguousStatusOptionIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new AmbiguousStatusOptionException("the Workflow board has more than one Development option")
        );
    }

    [Fact]
    public Task WorkflowStatusSetBoardWriteFailureIsReportedToTheCallerAsync()
    {
        return this.AssertWorkflowStatusSetFailureReportedAsync(
            new BoardWriteException("could not set the Workflow board status of " + REPOSITORY + "#41")
        );
    }

    [Fact]
    public async Task WorkflowStatusSetOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.WorkflowStatusSetServiceFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.WorkflowStatusSetAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private async Task AssertWorkflowStatusSetFailureReportedAsync(Exception failure)
    {
        this.WorkflowStatusSetServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.WorkflowStatusSetAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void WorkflowStatusSetServiceFailsWith(Exception failure)
    {
        this._workflowStatusSetService.SetAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                status: STATUS,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<WorkflowStatusSetResponse> WorkflowStatusSetAsync()
    {
        return WorkflowMcpTools.WorkflowStatusSetAsync(
            repo: REPOSITORY,
            status: STATUS,
            service: this._workflowStatusSetService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task AssertPrChecksFailureReportedAsync(Exception failure)
    {
        this.PrChecksServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.PrChecksAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void PrChecksServiceFailsWith(Exception failure)
    {
        this._prChecksService.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrChecksResponse> PrChecksAsync()
    {
        return WorkflowMcpTools.PrChecksAsync(
            repo: REPOSITORY,
            service: this._prChecksService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task AssertIssueViewFailureReportedAsync(Exception failure)
    {
        this.IssueServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueViewAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void IssueServiceFailsWith(Exception failure)
    {
        this._issueService.ViewAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueViewResponse> IssueViewAsync()
    {
        return WorkflowMcpTools.IssueViewAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            service: this._issueService,
            cancellationToken: this.CancellationToken()
        );
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
