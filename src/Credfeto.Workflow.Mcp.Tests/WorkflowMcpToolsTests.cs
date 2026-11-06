using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
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

    private const string ISSUE_TITLE = "feat(mcp): add issue create";

    private const string ISSUE_BODY = "Create an issue.";

    private const string PR_HEAD = "feat/28-pr-create";

    private const string PR_TITLE = "feat(mcp): add pr create";

    private const string PR_BODY = "Create a pull request.";

    private const string SEARCH_TEXT = "issue search";

    private const string BODY_TO_CHECK = "## Summary\n\nCheck this body.";

    private const string COMMENT_BODY = "## Summary\n\nThe comment text.";

    private readonly IBodyCheckService _bodyCheckService;
    private readonly ICommentAddService _commentAddService;
    private readonly IRequiredChecksReader _checksReader;
    private readonly IClosingIssueLabelsService _closingIssueLabelsService;
    private readonly IIssueCreateService _issueCreateService;
    private readonly IIssueListService _issueListService;
    private readonly IIssuePlanStatusReader _issuePlanStatusReader;
    private readonly IIssueSearchService _issueSearchService;
    private readonly IIssueService _issueService;
    private readonly ILabelService _labelService;
    private readonly IPullRequestListService _listService;
    private readonly IPrChecksService _prChecksService;
    private readonly IPrCreateService _prCreateService;
    private readonly IPrReplyService _prReplyService;
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
        this._listService = GetSubstitute<IPullRequestListService>();
        this._issueService = GetSubstitute<IIssueService>();
        this._issueCreateService = GetSubstitute<IIssueCreateService>();
        this._issueSearchService = GetSubstitute<IIssueSearchService>();
        this._issueListService = GetSubstitute<IIssueListService>();
        this._issuePlanStatusReader = GetSubstitute<IIssuePlanStatusReader>();
        this._labelService = GetSubstitute<ILabelService>();
        this._prChecksService = GetSubstitute<IPrChecksService>();
        this._prCreateService = GetSubstitute<IPrCreateService>();
        this._prReplyService = GetSubstitute<IPrReplyService>();
        this._bodyCheckService = GetSubstitute<IBodyCheckService>();
        this._commentAddService = GetSubstitute<ICommentAddService>();
        this._closingIssueLabelsService = GetSubstitute<IClosingIssueLabelsService>();
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
    public async Task PrListReturnsWhatTheServiceListedAsync()
    {
        IReadOnlyList<string> labels = ["bug", "Type: Feature"];
        PullRequestListResponse expected = new(Repository: REPOSITORY, PullRequests: []);
        this._listService.ListAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                head: BRANCH,
                labels: labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PullRequestListResponse actual = await WorkflowMcpTools.PrListAsync(
            repo: REPOSITORY,
            service: this._listService,
            issue: ISSUE,
            head: BRANCH,
            labels: labels,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrListPassesNoFiltersWhenNoneAreGivenAsync()
    {
        PullRequestListResponse expected = new(Repository: REPOSITORY, PullRequests: []);
        this._listService.ListAsync(
                repository: REPOSITORY,
                issue: null,
                head: null,
                labels: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PullRequestListResponse actual = await this.ListAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task PrListRepositoryNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertListFailureReportedAsync(
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            )
        );
    }

    [Fact]
    public Task PrListFailureIsReportedToTheCallerAsync()
    {
        return this.AssertListFailureReportedAsync(
            new PullRequestListFailedException("could not list the pull requests of " + REPOSITORY)
        );
    }

    [Fact]
    public Task PrListInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertListFailureReportedAsync(
            ParameterValidationException.For(parameterName: "issue", rule: "must be above 0")
        );
    }

    [Fact]
    public async Task PrListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ListServiceFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.ListAsync
        );

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
    public async Task IssuePlanStatusReturnsWhatTheReaderReadAsync()
    {
        IssuePlanStatus expected = new(
            HasPlan: true,
            Blocked: false,
            PlanAt: "2026-01-02T03:04:05Z",
            PlanBy: new(
                Login: "credfeto",
                Association: "OWNER",
                Url: new("https://github.com/" + REPOSITORY + "/issues/16#issuecomment-1")
            ),
            Comments: []
        );
        this._issuePlanStatusReader.ReadAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssuePlanStatus actual = await this.IssuePlanStatusAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task IssuePlanStatusIssueNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertIssuePlanStatusFailureReportedAsync(
            new IssueNotFoundException("issue " + REPOSITORY + "#16 was not found")
        );
    }

    [Fact]
    public Task IssuePlanStatusFailedReadIsReportedToTheCallerAsync()
    {
        return this.AssertIssuePlanStatusFailureReportedAsync(
            new IssueViewFailedException("could not read issue " + REPOSITORY + "#16")
        );
    }

    [Fact]
    public Task IssuePlanStatusInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertIssuePlanStatusFailureReportedAsync(
            ParameterValidationException.For(parameterName: "issue", rule: "must be above 0")
        );
    }

    [Fact]
    public async Task IssuePlanStatusOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssuePlanStatusFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssuePlanStatusAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task IssueCommentsReturnsWhatTheServiceReadAsync()
    {
        IssueComments expected = new(
            Issue: REPOSITORY + "#16",
            Url: new("https://github.com/" + REPOSITORY + "/issues/16"),
            Comments: [],
            Summary: new(Printed: 0, Matched: 0, Total: 0, Omitted: 0)
        );
        this._issueService.CommentsAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                since: "2026-01-02T03:04:05Z",
                author: "credfeto",
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueComments actual = await WorkflowMcpTools.IssueCommentsAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            service: this._issueService,
            since: "2026-01-02T03:04:05Z",
            author: "credfeto",
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public Task IssueCommentsIssueNotFoundIsReportedToTheCallerAsync()
    {
        return this.AssertIssueCommentsFailureReportedAsync(
            new IssueNotFoundException("issue " + REPOSITORY + "#16 was not found")
        );
    }

    [Fact]
    public Task IssueCommentsFailedReadIsReportedToTheCallerAsync()
    {
        return this.AssertIssueCommentsFailureReportedAsync(
            new IssueViewFailedException("could not read issue " + REPOSITORY + "#16")
        );
    }

    [Fact]
    public Task IssueCommentsIncompleteReadIsReportedToTheCallerAsync()
    {
        return this.AssertIssueCommentsFailureReportedAsync(
            new IssueCommentsIncompleteException("could not read all the comments of " + REPOSITORY + "#16")
        );
    }

    [Fact]
    public Task IssueCommentsInvalidParameterIsReportedToTheCallerAsync()
    {
        return this.AssertIssueCommentsFailureReportedAsync(
            ParameterValidationException.For(parameterName: "since", rule: "must be a UTC time")
        );
    }

    [Fact]
    public async Task IssueCommentsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueCommentsFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueCommentsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task IssueCloseReturnsWhatTheServiceClosedAsync()
    {
        IssueCloseResponse expected = CreateCloseResponse();
        this._issueService.CloseAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                reason: "not-planned",
                comment: "Superseded by #17",
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueCloseResponse actual = await WorkflowMcpTools.IssueCloseAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            reason: "not-planned",
            service: this._issueService,
            comment: "Superseded by #17",
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueClosePassesNoCommentWhenNoneIsGivenAsync()
    {
        IssueCloseResponse expected = CreateCloseResponse();
        this._issueService.CloseAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                reason: "completed",
                comment: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueCloseResponse actual = await this.IssueCloseAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueCloseFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in IssueCloseFailures())
        {
            await this.AssertIssueCloseFailureReportedAsync(failure);
        }
    }

    [Fact]
    public async Task IssueCloseOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueCloseFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueCloseAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> IssueCloseFailures()
    {
        return
        [
            new IssueNotFoundException("issue " + REPOSITORY + "#16 was not found"),
            new IssueCloseRefusedException("closing issue " + REPOSITORY + "#16 was refused"),
            new IssueCloseFailedException("could not close issue " + REPOSITORY + "#16"),
            ParameterValidationException.For(parameterName: "reason", rule: "must be completed or not-planned"),
        ];
    }

    private static IssueCloseResponse CreateCloseResponse()
    {
        return new(
            Issue: REPOSITORY + "#16",
            Outcome: "closed",
            Reason: "completed",
            Url: new("https://github.com/" + REPOSITORY + "/issues/16")
        );
    }

    private async Task AssertIssueCloseFailureReportedAsync(Exception failure)
    {
        this.IssueCloseFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueCloseAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void IssueCloseFailsWith(Exception failure)
    {
        this._issueService.CloseAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                reason: "completed",
                comment: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueCloseResponse> IssueCloseAsync()
    {
        return WorkflowMcpTools.IssueCloseAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            reason: "completed",
            service: this._issueService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task IssueCreateReturnsWhatTheServiceCreatedAsync()
    {
        IssueCreateResponse expected = new(new Uri("https://github.com/" + REPOSITORY + "/issues/90"));
        IReadOnlyList<string> labels = ["Bug"];
        this._issueCreateService.CreateAsync(
                repository: REPOSITORY,
                priority: "High",
                title: ISSUE_TITLE,
                body: ISSUE_BODY,
                labels: labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueCreateResponse actual = await WorkflowMcpTools.IssueCreateAsync(
            repo: REPOSITORY,
            priority: "High",
            title: ISSUE_TITLE,
            body: ISSUE_BODY,
            service: this._issueCreateService,
            labels: labels,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueCreatePassesNoLabelsWhenNoneAreGivenAsync()
    {
        IssueCreateResponse expected = new(new Uri("https://github.com/" + REPOSITORY + "/issues/91"));
        this._issueCreateService.CreateAsync(
                repository: REPOSITORY,
                priority: "Medium",
                title: ISSUE_TITLE,
                body: ISSUE_BODY,
                labels: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueCreateResponse actual = await this.IssueCreateAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueCreateFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in IssueCreateFailures())
        {
            this.IssueCreateFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueCreateAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task IssueCreateOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueCreateFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueCreateAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> IssueCreateFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "labels", rule: "it must contain a visible character"),
            new IssueCreateRefusedException("body refused: token (line 2)"),
            new IssueCreateFailedException("could not create the issue in " + REPOSITORY),
            new IssueCreateBoardWriteException(
                "could not put the issue on the board; the issue was already created: https://github.com/"
                    + REPOSITORY
                    + "/issues/90"
            ),
            new RepositoryNotFoundException("the repository being written to was not found"),
            new RepositoryVisibilityLookupFailedException(
                "the visibility of a referenced repository could not be read"
            ),
            new WorkflowBoardNotFoundException("no Workflow board"),
            new WorkflowBoardAmbiguousException("more than one Workflow board"),
            new WorkflowBoardInvalidException("unexpected project id/number"),
            new WorkflowStatusFieldNotFoundException("no Status field"),
            new StatusFieldReadFailedException("could not read the status fields of project 7"),
            new StatusOptionMissingException("no Not Started option"),
            new AmbiguousStatusOptionException("more than one Not Started option"),
        ];
    }

    private void IssueCreateFailsWith(Exception failure)
    {
        this._issueCreateService.CreateAsync(
                repository: REPOSITORY,
                priority: "Medium",
                title: ISSUE_TITLE,
                body: ISSUE_BODY,
                labels: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueCreateResponse> IssueCreateAsync()
    {
        return WorkflowMcpTools.IssueCreateAsync(
            repo: REPOSITORY,
            priority: "Medium",
            title: ISSUE_TITLE,
            body: ISSUE_BODY,
            service: this._issueCreateService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task IssueSearchSearchesOpenIssuesWithTheDefaultLimitWhenNeitherIsGivenAsync()
    {
        IssueSearchResult expected = SearchResult();
        this._issueSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: IssueSearchState.OPEN,
                limit: 30,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueSearchResult actual = await this.IssueSearchAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Theory]
    [InlineData("open", IssueSearchState.OPEN)]
    [InlineData("closed", IssueSearchState.CLOSED)]
    [InlineData("all", IssueSearchState.ALL)]
    public async Task IssueSearchPassesTheGivenStateAndLimitAsync(string state, IssueSearchState expectedState)
    {
        IssueSearchResult expected = SearchResult();
        this._issueSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: expectedState,
                limit: 5,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueSearchResult actual = await WorkflowMcpTools.IssueSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._issueSearchService,
            state: state,
            limit: 5,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueSearchRejectsAnUnknownStateWithoutSearchingAsync()
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            WorkflowMcpTools.IssueSearchAsync(
                repo: REPOSITORY,
                text: SEARCH_TEXT,
                service: this._issueSearchService,
                state: "merged",
                cancellationToken: this.CancellationToken()
            )
        );

        ParameterValidationException inner = Assert.IsType<ParameterValidationException>(exception.InnerException);
        Assert.Equal(expected: inner.Message, actual: exception.Message);
        Assert.Empty(this._issueSearchService.ReceivedCalls());
    }

    [Fact]
    public async Task IssueSearchFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in IssueSearchFailures())
        {
            this.IssueSearchFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueSearchAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task IssueSearchOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueSearchFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueSearchAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IssueSearchResult SearchResult()
    {
        return new(
            Repository: REPOSITORY,
            Issues: [new IssueSearchEntry(Number: 19, State: "OPEN", Title: "issue search", Labels: ["feature"])],
            Truncated: true
        );
    }

    private static IReadOnlyList<Exception> IssueSearchFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "limit", rule: "it must be from 1 to 100"),
            new IssueSearchRefusedException("text refused: token"),
            new IssueSearchFailedException("could not search the issues of " + REPOSITORY),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
        ];
    }

    private void IssueSearchFailsWith(Exception failure)
    {
        this._issueSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: IssueSearchState.OPEN,
                limit: 30,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueSearchResult> IssueSearchAsync()
    {
        return WorkflowMcpTools.IssueSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._issueSearchService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task IssueListReturnsWhatTheServiceListedAsync()
    {
        IssueListResponse expected = new(
            Repository: REPOSITORY,
            Issues:
            [
                new IssueSummary(
                    Number: 18,
                    Priority: "High",
                    Blocked: false,
                    OnHold: true,
                    Assignees: ["credfeto"],
                    Title: "issue list",
                    Labels: ["High", "On Hold"]
                ),
            ]
        );
        this._issueListService.ListOpenIssuesAsync(
                repository: REPOSITORY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        IssueListResponse actual = await this.IssueListAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task IssueListFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in IssueListFailures())
        {
            this.IssueListFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueListAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task IssueListOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.IssueListFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.IssueListAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> IssueListFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repo", rule: "it must be owner/name"),
            new IssueListFailedException("could not list the open issues of " + REPOSITORY),
            new RepositoryNotFoundException(
                "the repository " + REPOSITORY + " was not found, or the caller cannot see it"
            ),
        ];
    }

    private void IssueListFailsWith(Exception failure)
    {
        this._issueListService.ListOpenIssuesAsync(
                repository: REPOSITORY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueListResponse> IssueListAsync()
    {
        return WorkflowMcpTools.IssueListAsync(
            repo: REPOSITORY,
            service: this._issueListService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task PrCreateReturnsWhatTheServiceCreatedAsync()
    {
        PrCreateResponse expected = new(
            Url: new Uri("https://github.com/" + REPOSITORY + "/pull/93"),
            Created: true,
            Note: null
        );
        this._prCreateService.CreateAsync(
                repository: REPOSITORY,
                head: PR_HEAD,
                baseBranch: BRANCH,
                title: PR_TITLE,
                body: PR_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrCreateResponse actual = await WorkflowMcpTools.PrCreateAsync(
            repo: REPOSITORY,
            head: PR_HEAD,
            title: PR_TITLE,
            body: PR_BODY,
            service: this._prCreateService,
            @base: BRANCH,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrCreatePassesNoBaseWhenNoneIsGivenAsync()
    {
        PrCreateResponse expected = new(
            Url: new Uri("https://github.com/" + REPOSITORY + "/pull/94"),
            Created: false,
            Note: "an open pull request already exists for the branch"
        );
        this._prCreateService.CreateAsync(
                repository: REPOSITORY,
                head: PR_HEAD,
                baseBranch: null,
                title: PR_TITLE,
                body: PR_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrCreateResponse actual = await this.PrCreateAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrCreateFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in PrCreateFailures())
        {
            this.PrCreateFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.PrCreateAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task PrCreateOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrCreateFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrCreateAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> PrCreateFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "head", rule: "it must be a valid branch name"),
            new PrCreateRefusedException("body refused: token (line 2)"),
            new PrCreateFailedException("could not create the pull request in " + REPOSITORY),
            new PrCreateBoardWriteException(
                "could not put the pull request on the board; the pull request was already created: https://github.com/"
                    + REPOSITORY
                    + "/pull/93"
            ),
            new RepositoryNotFoundException("the repository being written to was not found"),
            new RepositoryVisibilityLookupFailedException(
                "the visibility of a referenced repository could not be read"
            ),
            new PullRequestListFailedException("could not list the pull requests of " + REPOSITORY),
            new WorkflowBoardNotFoundException("no Workflow board"),
            new WorkflowBoardAmbiguousException("more than one Workflow board"),
            new WorkflowBoardInvalidException("unexpected project id/number"),
            new WorkflowStatusFieldNotFoundException("no Status field"),
            new StatusFieldReadFailedException("could not read the status fields of project 7"),
            new StatusOptionMissingException("no Development option"),
            new AmbiguousStatusOptionException("more than one Development option"),
        ];
    }

    private void PrCreateFailsWith(Exception failure)
    {
        this._prCreateService.CreateAsync(
                repository: REPOSITORY,
                head: PR_HEAD,
                baseBranch: null,
                title: PR_TITLE,
                body: PR_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrCreateResponse> PrCreateAsync()
    {
        return WorkflowMcpTools.PrCreateAsync(
            repo: REPOSITORY,
            head: PR_HEAD,
            title: PR_TITLE,
            body: PR_BODY,
            service: this._prCreateService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task PrReplyReturnsWhatTheServiceAnsweredAsync()
    {
        PrReplyResult expected = new(
            Outcome: PrReplyOutcome.Posted,
            Message: "the reply was posted to the thread of comment 123",
            CommentId: 456,
            InReplyToId: 123,
            Url: new("https://github.com/" + REPOSITORY + "/pull/41#discussion_r456"),
            Rule: null,
            LineNumber: null
        );
        this.PrReplyServiceCallAsync().Returns(expected);

        PrReplyResult actual = await this.PrReplyAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrReplyValidationFailuresAreReportedToTheCallerAsync()
    {
        ParameterValidationException failure = ParameterValidationException.For(
            parameterName: "body",
            rule: "it must not be empty or whitespace"
        );
        this.PrReplyServiceCallAsync().ThrowsAsync(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.PrReplyAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task PrReplyOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrReplyServiceCallAsync().ThrowsAsync(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrReplyAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private ValueTask<PrReplyResult> PrReplyServiceCallAsync()
    {
        return this._prReplyService.ReplyAsync(
            repository: REPOSITORY,
            pullRequest: PULL_REQUEST,
            commentId: "123",
            body: "Thanks",
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private Task<PrReplyResult> PrReplyAsync()
    {
        return WorkflowMcpTools.PrReplyAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            commentId: "123",
            body: "Thanks",
            service: this._prReplyService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task CommentAddReturnsThePostedCommentAsync()
    {
        CommentAddResult expected = new(
            Outcome: CommentAddOutcome.Posted,
            Message: "the comment was posted on " + REPOSITORY + "#41",
            Url: new("https://github.com/" + REPOSITORY + "/pull/41#issuecomment-99"),
            Rule: null,
            LineNumber: null
        );
        this.CommentAddReturns(expected);

        CommentAddResult actual = await this.CommentAddAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task CommentAddReturnsARefusalAsAResultAsync()
    {
        CommentAddResult expected = new(
            Outcome: CommentAddOutcome.Refused,
            Message: "body refused: approval keyword (line 3)",
            Url: null,
            Rule: "approval keyword",
            LineNumber: 3
        );
        this.CommentAddReturns(expected);

        CommentAddResult actual = await this.CommentAddAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task CommentAddFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in CommentAddFailures())
        {
            this.CommentAddFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.CommentAddAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task CommentAddOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.CommentAddFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.CommentAddAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> CommentAddFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "pr", rule: "exactly one of pr or issue must be given"),
            new ParameterValidationException("the body is empty"),
            new RepositoryNotFoundException("the repository being written to was not found"),
            new RepositoryVisibilityLookupFailedException(
                "the visibility of a referenced repository could not be read"
            ),
            new LeakCheckFailedException("the text could not be checked for leaks in time"),
        ];
    }

    private void CommentAddReturns(CommentAddResult result)
    {
        this._commentAddService.AddAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                body: COMMENT_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(result);
    }

    private void CommentAddFailsWith(Exception failure)
    {
        this._commentAddService.AddAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                body: COMMENT_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<CommentAddResult> CommentAddAsync()
    {
        return WorkflowMcpTools.CommentAddAsync(
            repo: REPOSITORY,
            body: COMMENT_BODY,
            service: this._commentAddService,
            pr: PULL_REQUEST,
            issue: null,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task BodyCheckReturnsAPassAsync()
    {
        BodyCheckResult expected = new(Outcome: BodyCheckOutcome.Passed, Rule: null, Line: null, Message: null);
        this.BodyCheckReturns(expected);

        BodyCheckResult actual = await this.BodyCheckAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task BodyCheckReturnsARefusalAsAResultAsync()
    {
        BodyCheckResult expected = new(
            Outcome: BodyCheckOutcome.Refused,
            Rule: "host path",
            Line: 3,
            Message: "body refused: host path (line 3)"
        );
        this.BodyCheckReturns(expected);

        BodyCheckResult actual = await this.BodyCheckAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task BodyCheckFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in BodyCheckFailures())
        {
            this.BodyCheckFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.BodyCheckAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task BodyCheckOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.BodyCheckFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.BodyCheckAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> BodyCheckFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            new ParameterValidationException("the body is empty"),
            new RepositoryNotFoundException("the repository being written to was not found"),
            new RepositoryVisibilityLookupFailedException(
                "the visibility of a referenced repository could not be read"
            ),
            new LeakCheckFailedException("the text could not be checked for leaks in time"),
        ];
    }

    private void BodyCheckReturns(BodyCheckResult result)
    {
        this._bodyCheckService.CheckAsync(
                repository: REPOSITORY,
                body: BODY_TO_CHECK,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(result);
    }

    private void BodyCheckFailsWith(Exception failure)
    {
        this._bodyCheckService.CheckAsync(
                repository: REPOSITORY,
                body: BODY_TO_CHECK,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<BodyCheckResult> BodyCheckAsync()
    {
        return WorkflowMcpTools.BodyCheckAsync(
            repo: REPOSITORY,
            body: BODY_TO_CHECK,
            service: this._bodyCheckService,
            cancellationToken: this.CancellationToken()
        );
    }

    [Fact]
    public async Task ClosingIssueLabelsReturnsWhatTheServiceReadAsync()
    {
        ClosingIssueLabelsResponse expected = new(Labels: ["enhancement"], Complete: true, FailedReferences: []);
        this.ClosingIssueLabelsReturns(expected);

        ClosingIssueLabelsResponse actual = await this.ClosingIssueLabelsAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task ClosingIssueLabelsFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in ClosingIssueLabelsFailures())
        {
            this.ClosingIssueLabelsFailsWith(failure);

            McpException exception = await Assert.ThrowsAsync<McpException>(this.ClosingIssueLabelsAsync);

            Assert.Equal(expected: failure.Message, actual: exception.Message);
            Assert.Same(expected: failure, actual: exception.InnerException);
        }
    }

    [Fact]
    public async Task ClosingIssueLabelsOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.ClosingIssueLabelsFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.ClosingIssueLabelsAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> ClosingIssueLabelsFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "repository", rule: "it must be in the form owner/repo"),
            ParameterValidationException.For(parameterName: "pullRequest", rule: "must be above 0"),
            new ClosingIssueLabelsFailedException(
                "could not read the closing issues of credfeto/credfeto-workflow-mcp#41"
            ),
        ];
    }

    private void ClosingIssueLabelsReturns(ClosingIssueLabelsResponse result)
    {
        this._closingIssueLabelsService.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(result);
    }

    private void ClosingIssueLabelsFailsWith(Exception failure)
    {
        this._closingIssueLabelsService.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<ClosingIssueLabelsResponse> ClosingIssueLabelsAsync()
    {
        return WorkflowMcpTools.ClosingIssueLabelsAsync(
            repo: REPOSITORY,
            pr: PULL_REQUEST,
            service: this._closingIssueLabelsService,
            cancellationToken: this.CancellationToken()
        );
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

    private async Task AssertIssuePlanStatusFailureReportedAsync(Exception failure)
    {
        this.IssuePlanStatusFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.IssuePlanStatusAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void IssuePlanStatusFailsWith(Exception failure)
    {
        this._issuePlanStatusReader.ReadAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssuePlanStatus> IssuePlanStatusAsync()
    {
        return WorkflowMcpTools.IssuePlanStatusAsync(
            repo: REPOSITORY,
            issue: ISSUE,
            reader: this._issuePlanStatusReader,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task AssertIssueCommentsFailureReportedAsync(Exception failure)
    {
        this.IssueCommentsFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.IssueCommentsAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void IssueCommentsFailsWith(Exception failure)
    {
        this._issueService.CommentsAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                since: null,
                author: null,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<IssueComments> IssueCommentsAsync()
    {
        return WorkflowMcpTools.IssueCommentsAsync(
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

    [Fact]
    public async Task LabelAddReturnsWhatTheServiceChangedAsync()
    {
        IReadOnlyList<string> labels = ["Bug"];
        LabelChangeResponse expected = new(Repository: REPOSITORY, Number: PULL_REQUEST, Labels: ["Bug"]);
        this._labelService.AddAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                issue: null,
                labels: labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await WorkflowMcpTools.LabelAddAsync(
            repo: REPOSITORY,
            labels: labels,
            service: this._labelService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task LabelRemoveReturnsWhatTheServiceChangedAsync()
    {
        IReadOnlyList<string> labels = ["Bug"];
        LabelChangeResponse expected = new(Repository: REPOSITORY, Number: ISSUE, Labels: []);
        this._labelService.RemoveAsync(
                repository: REPOSITORY,
                pullRequest: null,
                issue: ISSUE,
                labels: labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await WorkflowMcpTools.LabelRemoveAsync(
            repo: REPOSITORY,
            labels: labels,
            service: this._labelService,
            issue: ISSUE,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task LabelFailuresAreReportedToTheCallerAsync()
    {
        foreach (Exception failure in LabelFailures())
        {
            await this.AssertLabelAddFailureReportedAsync(failure);
            await this.AssertLabelRemoveFailureReportedAsync(failure);
        }
    }

    [Fact]
    public async Task LabelAddOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.LabelAddFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.LabelAddAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task LabelRemoveOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.LabelRemoveFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.LabelRemoveAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static IReadOnlyList<Exception> LabelFailures()
    {
        return
        [
            new LabelRefusedException("the label Urgent was refused: a priority label may only be changed by a human"),
            new LabelChangeFailedException("could not change the labels of " + REPOSITORY + "#41"),
            new PullRequestNotFoundException("pull request " + REPOSITORY + "#41 was not found"),
            new IssueNotFoundException("issue " + REPOSITORY + "#16 was not found"),
            ParameterValidationException.For(parameterName: "labels", rule: "at least one label must be given"),
        ];
    }

    private async Task AssertLabelAddFailureReportedAsync(Exception failure)
    {
        this.LabelAddFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.LabelAddAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private async Task AssertLabelRemoveFailureReportedAsync(Exception failure)
    {
        this.LabelRemoveFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.LabelRemoveAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void LabelAddFailsWith(Exception failure)
    {
        this._labelService.AddAsync(
                repository: REPOSITORY,
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                labels: Arg.Any<IReadOnlyList<string>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private void LabelRemoveFailsWith(Exception failure)
    {
        this._labelService.RemoveAsync(
                repository: REPOSITORY,
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                labels: Arg.Any<IReadOnlyList<string>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<LabelChangeResponse> LabelAddAsync()
    {
        return WorkflowMcpTools.LabelAddAsync(
            repo: REPOSITORY,
            labels: ["Bug"],
            service: this._labelService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    private Task<LabelChangeResponse> LabelRemoveAsync()
    {
        return WorkflowMcpTools.LabelRemoveAsync(
            repo: REPOSITORY,
            labels: ["Bug"],
            service: this._labelService,
            pr: PULL_REQUEST,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task AssertListFailureReportedAsync(Exception failure)
    {
        this.ListServiceFailsWith(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(this.ListAsync);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    private void ListServiceFailsWith(Exception failure)
    {
        this._listService.ListAsync(
                repository: REPOSITORY,
                issue: Arg.Any<int?>(),
                head: Arg.Any<string?>(),
                labels: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PullRequestListResponse> ListAsync()
    {
        return WorkflowMcpTools.PrListAsync(
            repo: REPOSITORY,
            service: this._listService,
            cancellationToken: this.CancellationToken()
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
