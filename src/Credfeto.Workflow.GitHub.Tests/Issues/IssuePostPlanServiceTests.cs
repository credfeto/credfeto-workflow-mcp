using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection. The real leak checker is used, so each refusal is the one the rules give
public sealed class IssuePostPlanServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 173;
    private const string REFERENCE = REPOSITORY + "#173";
    private const string BODY = "## Implementation Plan\n\nDo the work.";
    private const string SECRET = "gh" + "p_abcdef";
    private const string COMMENT_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/173#issuecomment-1";
    private const string OPEN_ISSUE = "{\"state\":\"open\",\"labels\":[{\"name\":\"AI-Work\"}],\"pull_request\":null}";

    private static readonly Uri CommentUri = new(uriString: COMMENT_URL, uriKind: UriKind.Absolute);

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/173");

    private readonly ICommentAddService _commentAddService;
    private readonly ILabelService _labelService;
    private readonly IRepositoryVisibilityLookup _visibilityLookup;
    private readonly IWorkflowStatusSetService _workflowStatusSetService;

    public IssuePostPlanServiceTests()
    {
        this._visibilityLookup = GetSubstitute<IRepositoryVisibilityLookup>();
        this._commentAddService = GetSubstitute<ICommentAddService>();
        this._labelService = GetSubstitute<ILabelService>();
        this._workflowStatusSetService = GetSubstitute<IWorkflowStatusSetService>();
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Reading(Func<HttpResponseMessage> read)
    {
        return new(index =>
            index switch
            {
                0 => read(),
                _ => Unexpected(index),
            }
        );
    }

    private static RecordingHttpMessageHandler ReadingIssue(string json)
    {
        return Reading(() => Responses.Json(json));
    }

    private IssuePostPlanService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        LeakChecker leakChecker = new(
            visibilityLookup: this._visibilityLookup,
            options: Options.Create(new WorkflowOptions { PrivateOwners = "secretorg" })
        );

        return new(
            restClient: rest,
            leakChecker: leakChecker,
            commentAddService: this._commentAddService,
            labelService: this._labelService,
            workflowStatusSetService: this._workflowStatusSetService
        );
    }

    private ValueTask<IssuePostPlanResponse> PostAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int issue = ISSUE,
        string body = BODY
    )
    {
        return this.CreateService(upstream)
            .PostAsync(repository: repository, issue: issue, body: body, cancellationToken: this.CancellationToken());
    }

    private void CommentReturns(CommentAddResult result)
    {
        this._commentAddService.AddAsync(
                repository: REPOSITORY,
                pullRequest: null,
                issue: ISSUE,
                body: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(result);
    }

    private void CommentIsPosted()
    {
        this.CommentReturns(
            new(
                Outcome: CommentAddOutcome.Posted,
                Message: "the comment was posted on " + REFERENCE,
                Url: CommentUri,
                Rule: null,
                LineNumber: null
            )
        );
    }

    private void BlockedIsAdded()
    {
        this.BlockedCallAsync()
            .Returns(new LabelChangeResponse(Repository: REPOSITORY, Number: ISSUE, Labels: ["AI-Work", "Blocked"]));
    }

    private ValueTask<LabelChangeResponse> BlockedCallAsync()
    {
        return this._labelService.AddAsync(
            repository: REPOSITORY,
            pullRequest: null,
            issue: ISSUE,
            labels: Arg.Is<IReadOnlyList<string>>(labels => labels.Count == 1 && labels[0] == StandardLabels.BLOCKED),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private ValueTask<WorkflowStatusSetResponse> PlanningCallAsync()
    {
        return this._workflowStatusSetService.SetAsync(
            repository: REPOSITORY,
            pullRequest: null,
            issue: ISSUE,
            status: "Planning",
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private void PlanningIsSet()
    {
        this.PlanningCallAsync()
            .Returns(
                new WorkflowStatusSetResponse(
                    ItemUrl: new(
                        uriString: "https://github.com/" + REPOSITORY + "/issues/173",
                        uriKind: UriKind.Absolute
                    ),
                    Status: "Planning"
                )
            );
    }

    private async Task NothingWasWrittenAsync()
    {
        await this
            ._commentAddService.DidNotReceive()
            .AddAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                body: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this.NoLabelOrStatusWasWrittenAsync();
    }

    private async Task NoLabelOrStatusWasWrittenAsync()
    {
        await this
            ._labelService.DidNotReceive()
            .AddAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                labels: Arg.Any<IReadOnlyList<string>>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this.NoStatusWasWrittenAsync();
    }

    private async Task NoStatusWasWrittenAsync()
    {
        await this
            ._workflowStatusSetService.DidNotReceive()
            .SetAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                status: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private async Task AssertRefusedAsync(RecordingHttpMessageHandler upstream, string body, string message)
    {
        IssuePostPlanRefusedException exception = await Assert.ThrowsAsync<IssuePostPlanRefusedException>(async () =>
            await this.PostAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: message, actual: exception.Message);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task APlanIsPostedThenBlockedThenPlanningAsync()
    {
        this.CommentIsPosted();
        this.BlockedIsAdded();
        this.PlanningIsSet();
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        IssuePostPlanResponse response = await this.PostAsync(upstream);

        Assert.Equal(expected: CommentUri, actual: response.CommentUrl);
        Assert.Equal(expected: ["AI-Work", "Blocked"], actual: response.Labels);
        Assert.Equal(expected: "Planning", actual: response.Board);
        Assert.Equal(expected: IssueUri, actual: Assert.Single(upstream.Requests).RequestUri);

        Received.InOrder(() =>
        {
            _ = this
                ._commentAddService.AddAsync(
                    repository: REPOSITORY,
                    pullRequest: null,
                    issue: ISSUE,
                    body: BODY,
                    cancellationToken: Arg.Any<CancellationToken>()
                )
                .AsTask();
            _ = this.BlockedCallAsync().AsTask();
            _ = this.PlanningCallAsync().AsTask();
        });
    }

    [Fact]
    public async Task ARepositoryWithoutABoardIsReportedAsNoBoardAsync()
    {
        this.CommentIsPosted();
        this.BlockedIsAdded();
        this.PlanningCallAsync().ThrowsAsync(new WorkflowBoardNotFoundException("no Workflow board for " + REPOSITORY));
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        IssuePostPlanResponse response = await this.PostAsync(upstream);

        Assert.Equal(expected: CommentUri, actual: response.CommentUrl);
        Assert.Equal(expected: "no board", actual: response.Board);
    }

    [Fact]
    public async Task ALeakIsRefusedWithItsLineBeforeTheIssueIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await this.AssertRefusedAsync(
            upstream: upstream,
            body: BODY + "\ntoken " + SECRET,
            message: "body refused: token (line 4)"
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AnApprovalKeywordIsRefusedWithItsLineBeforeTheIssueIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await this.AssertRefusedAsync(
            upstream: upstream,
            body: BODY + "\nApproved",
            message: "body refused: approval keyword (line 4)"
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("## Summary\n\nDo the work.")]
    [InlineData("## Implementation Plan:\n\nDo the work.")]
    [InlineData("  ## Implementation Plan\n\nDo the work.")]
    [InlineData("Some text ## Implementation Plan")]
    public async Task ABodyWithoutTheExactPlanHeadingIsRefusedBeforeTheIssueIsReadAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await this.AssertRefusedAsync(
            upstream: upstream,
            body: body,
            message: "post-plan refused: no '## Implementation Plan' line"
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AClosedIssueIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadingIssue("{\"state\":\"closed\",\"labels\":[]}");

        await this.AssertRefusedAsync(
            upstream: upstream,
            body: BODY,
            message: "post-plan refused: the issue is not open"
        );
    }

    [Theory]
    [InlineData("On Hold")]
    [InlineData("on hold")]
    [InlineData("On-Hold")]
    public async Task AnIssueOnHoldIsRefusedAsync(string label)
    {
        using RecordingHttpMessageHandler upstream = ReadingIssue(
            "{\"state\":\"open\",\"labels\":[{\"name\":\"AI-Work\"},{\"name\":\"" + label + "\"}]}"
        );

        await this.AssertRefusedAsync(upstream: upstream, body: BODY, message: "post-plan refused: On Hold label");
    }

    [Fact]
    public async Task APullRequestIsReportedAsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadingIssue(
            "{\"state\":\"open\",\"labels\":[],\"pull_request\":{\"url\":\"https://api.github.com/repos/"
                + REPOSITORY
                + "/pulls/173\"}}"
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(expected: "issue " + REFERENCE + " was not found", actual: exception.Message);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AMissingIssueIsReportedAsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Reading(() =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.NotFound, json: "{\"message\":\"Not Found\"}")
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(expected: "issue " + REFERENCE + " was not found", actual: exception.Message);
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData("{\"state\":\"unknown\",\"labels\":[]}")]
    [InlineData("{\"labels\":[]}")]
    [InlineData("{\"state\":\"open\",\"labels\":[{\"name\":null}]}")]
    public async Task AnUnreadableIssueIsReportedAsAReadFailureAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = ReadingIssue(json);

        IssueViewFailedException exception = await Assert.ThrowsAsync<IssueViewFailedException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(expected: "could not read issue " + REFERENCE, actual: exception.Message);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AServerErrorReadingTheIssueIsReportedAsAReadFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        IssueViewFailedException exception = await Assert.ThrowsAsync<IssueViewFailedException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(expected: "could not read issue " + REFERENCE, actual: exception.Message);
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData("", 173, "repository")]
    [InlineData(REPOSITORY, 0, "issue")]
    [InlineData(REPOSITORY, -1, "issue")]
    public async Task InvalidArgumentsAreRefusedBeforeAnyCallAsync(string repository, int issue, string parameterName)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(async () =>
            await this.PostAsync(upstream: upstream, repository: repository, issue: issue)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    public async Task AnEmptyBodyIsRefusedBeforeAnyCallAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(async () =>
            await this.PostAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Equal(expected: "the body is empty", actual: exception.Message);
        Assert.Empty(upstream.Requests);
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData(CommentAddOutcome.NotPosted, "could not post the comment on " + REFERENCE + ": 502 Bad Gateway", null)]
    [InlineData(
        CommentAddOutcome.MayHaveBeenPosted,
        "could not post the comment on " + REFERENCE + ", which may have been posted; read the comments first",
        null
    )]
    [InlineData(CommentAddOutcome.Refused, "body refused: approval keyword (line 3)", null)]
    [InlineData(CommentAddOutcome.Posted, "the comment was posted on " + REFERENCE, null)]
    [InlineData(CommentAddOutcome.NotPosted, "could not post the comment on " + REFERENCE, COMMENT_URL)]
    public async Task ACommentThatWasNotPostedWritesNothingElseAsync(
        CommentAddOutcome outcome,
        string message,
        string? url
    )
    {
        this.CommentReturns(
            new(
                Outcome: outcome,
                Message: message,
                Url: url is null ? null : new Uri(uriString: url, uriKind: UriKind.Absolute),
                Rule: null,
                LineNumber: null
            )
        );
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        IssuePostPlanFailedException exception = await Assert.ThrowsAsync<IssuePostPlanFailedException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(expected: message, actual: exception.Message);
        Assert.Null(exception.CommentUrl);
        Assert.Empty(exception.StepsDone);
        await this.NoLabelOrStatusWasWrittenAsync();
    }

    [Fact]
    public async Task ALabelFailureNamesTheCommentPostedAsync()
    {
        LabelChangeFailedException failure = new("could not add labels to " + REFERENCE);
        this.CommentIsPosted();
        this.BlockedCallAsync().ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        IssuePostPlanFailedException exception = await Assert.ThrowsAsync<IssuePostPlanFailedException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(
            expected: "could not add the Blocked label; steps done: comment posted " + COMMENT_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: CommentUri, actual: exception.CommentUrl);
        Assert.Equal(expected: ["comment posted " + COMMENT_URL], actual: exception.StepsDone);
        Assert.Same(expected: failure, actual: exception.InnerException);
        await this.NoStatusWasWrittenAsync();
    }

    [Fact]
    public async Task ABoardFailureNamesTheCommentPostedAndBlockedAddedAsync()
    {
        WorkflowBoardAmbiguousException failure = new("more than one Workflow board for " + REPOSITORY);
        this.CommentIsPosted();
        this.BlockedIsAdded();
        this.PlanningCallAsync().ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        IssuePostPlanFailedException exception = await Assert.ThrowsAsync<IssuePostPlanFailedException>(async () =>
            await this.PostAsync(upstream)
        );

        Assert.Equal(
            expected: "could not set the Workflow status to Planning; steps done: comment posted "
                + COMMENT_URL
                + ", Blocked added",
            actual: exception.Message
        );
        Assert.Equal(expected: CommentUri, actual: exception.CommentUrl);
        Assert.Equal(expected: ["comment posted " + COMMENT_URL, "Blocked added"], actual: exception.StepsDone);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task ACancellationAfterTheCommentIsNotReportedAsAPartWayFailureAsync()
    {
        this.CommentIsPosted();
        this.BlockedCallAsync().ThrowsAsync(new OperationCanceledException());
        using RecordingHttpMessageHandler upstream = ReadingIssue(OPEN_ISSUE);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await this.PostAsync(upstream));
        await this.NoStatusWasWrittenAsync();
    }
}
