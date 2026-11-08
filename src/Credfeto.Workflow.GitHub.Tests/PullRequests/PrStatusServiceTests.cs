using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using FunFair.Test.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrStatusServiceTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PR = 73;
    private const string REFERENCE = REPOSITORY + "#73";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string OTHER_SHA = "89abcdef0123456789abcdef0123456789abcdef";
    private const string READY_AT = "2026-01-02T00:00:00Z";

    private static readonly Uri PullUrl = new("https://github.com/" + REPOSITORY + "/pull/73");

    private readonly IPrChecksService _checks;
    private readonly IPullRequestCommentsReader _comments;
    private readonly IPrReadyAtService _readyAt;
    private readonly IWorkflowStatusCheckService _statusCheck;
    private readonly IPullRequestService _view;

    public PrStatusServiceTests()
    {
        this._view = GetSubstitute<IPullRequestService>();
        this._checks = GetSubstitute<IPrChecksService>();
        this._statusCheck = GetSubstitute<IWorkflowStatusCheckService>();
        this._comments = GetSubstitute<IPullRequestCommentsReader>();
        this._readyAt = GetSubstitute<IPrReadyAtService>();

        this.GivenView(View());
        this.GivenChecks(Checks(verdict: PrChecksVerdict.Passed, sha: SHA, Check("build", PrChecksVerdict.Passed)));
        this.GivenBoard(new(Outcome: WorkflowStatusCheckOutcome.Found, State: "AI Review", Message: null));
        this.GivenComments(Comments(comments: [], reviews: []));
        this.GivenReadyAt(new(ReadyAt: READY_AT, Source: PrReadyAtService.SOURCE_READY_FOR_REVIEW));
    }

    private static PullRequestView View(string draft = "no", string? headSha = SHA, int? changedFiles = 4)
    {
        return new(
            Pr: REFERENCE,
            State: "OPEN",
            Draft: draft,
            Title: "Add the status tool",
            Url: PullUrl,
            Base: "main",
            Head: "feat/73-pr-status-tool",
            HeadSha: headSha,
            AutoMerge: "off",
            ReviewDecision: "REVIEW_REQUIRED",
            MergeState: "BLOCKED",
            Mergeable: "MERGEABLE",
            CreatedAt: null,
            MergedAt: null,
            MergeCommit: null,
            Labels: "enhancement",
            Assignees: "credfeto",
            Author: "credfeto",
            CommitAuthors: null,
            ChangedFiles: changedFiles,
            Files: null,
            Body: null
        );
    }

    private static PrCheckResult Check(string name, PrChecksVerdict verdict)
    {
        return new(Name: name, Verdict: verdict, CompletedAt: null, RunId: null);
    }

    private static PrChecksResponse Checks(PrChecksVerdict verdict, string sha, params PrCheckResult[] checks)
    {
        return new(Repo: REPOSITORY, Sha: sha, Verdict: verdict, Passed: 0, Failed: 0, Pending: 0, Checks: checks);
    }

    private static PullRequestComment Comment(string author, string createdAt)
    {
        return new(
            Id: "IC_" + author,
            Author: author,
            Association: "MEMBER",
            CreatedAt: createdAt,
            Minimized: false,
            Url: PullUrl,
            Body: "body"
        );
    }

    private static PullRequestReview Review(string author, string state, string submittedAt)
    {
        return new(
            Id: "PRR_" + author,
            Author: author,
            Association: "MEMBER",
            State: state,
            SubmittedAt: submittedAt,
            Url: PullUrl,
            Body: string.Empty
        );
    }

    private static PullRequestComments Comments(
        IReadOnlyList<PullRequestComment> comments,
        IReadOnlyList<PullRequestReview> reviews
    )
    {
        return new(
            Pr: REFERENCE,
            ReviewDecision: "REVIEW_REQUIRED",
            Comments: comments,
            Reviews: reviews,
            Threads: [],
            Summary: new(
                Comments: comments.Count,
                Reviews: reviews.Count,
                ReviewsEmpty: 0,
                Threads: 5,
                Unresolved: 2,
                Inline: 7
            )
        );
    }

    private void GivenView(PullRequestView view)
    {
        this._view.ViewAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                fields: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(view);
    }

    private void GivenChecks(PrChecksResponse checks)
    {
        this._checks.ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                sha: Arg.Any<string?>(),
                branch: Arg.Any<string?>(),
                all: Arg.Any<bool>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(checks);
    }

    private void GivenBoard(WorkflowStatusCheckResult result)
    {
        this._statusCheck.CheckAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(result);
    }

    private void GivenBoardFails(Exception exception)
    {
        this._statusCheck.CheckAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int?>(),
                issue: Arg.Any<int?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(exception);
    }

    private void GivenComments(PullRequestComments comments)
    {
        this._comments.ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(comments);
    }

    private void GivenReadyAt(PrReadyAtResponse readyAt)
    {
        this._readyAt.ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(readyAt);
    }

    private Task<PrStatusResponse> ReadAsync()
    {
        PrStatusService service = new(
            viewService: this._view,
            checksService: this._checks,
            statusCheckService: this._statusCheck,
            commentsReader: this._comments,
            readyAtService: this._readyAt
        );

        return service
            .ReadAsync(repository: REPOSITORY, pullRequest: PR, cancellationToken: this.CancellationToken())
            .AsTask();
    }

    [Fact]
    public async Task AReadyPullRequestWithPassingChecksIsSummarisedAsync()
    {
        PrStatusResponse status = await this.ReadAsync();

        PrStatusResponse expected = new(
            Pr: REFERENCE,
            State: "OPEN",
            Draft: "no",
            Title: "Add the status tool",
            Url: PullUrl,
            Base: "main",
            Head: "feat/73-pr-status-tool",
            HeadSha: SHA,
            AutoMerge: "off",
            ReviewDecision: "REVIEW_REQUIRED",
            MergeState: "BLOCKED",
            Mergeable: "MERGEABLE",
            Labels: "enhancement",
            Assignees: "credfeto",
            Author: "credfeto",
            ChangedFiles: 4,
            Board: "AI Review",
            ReadyAt: READY_AT,
            ReadySource: PrReadyAtService.SOURCE_READY_FOR_REVIEW,
            Checks: status.Checks,
            Activity: new(Comments: 0, Reviews: 0, Threads: 5, Unresolved: 2, SinceReady: 0, Last: null)
        );

        Assert.Equal(expected: expected, actual: status);
        Assert.Equal(expected: PrChecksVerdict.Passed, actual: status.Checks.Verdict);
        Assert.Empty(status.Checks.Failing);
        Assert.Empty(status.Checks.Pending);
    }

    [Fact]
    public async Task OnlyTheFieldsTheStatusNeedsAreReadAsync()
    {
        await this.ReadAsync();

        await this
            ._view.Received(1)
            .ViewAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                fields: Arg.Is<IReadOnlyList<string>?>(fields => AreTheStatusFields(fields)),
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this
            ._checks.Received(1)
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this
            ._statusCheck.Received(1)
            .CheckAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                issue: null,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task FailingAndPendingChecksAreNamedAsync()
    {
        this.GivenChecks(
            Checks(
                PrChecksVerdict.Failed,
                SHA,
                Check("build", PrChecksVerdict.Failed),
                Check("lint", PrChecksVerdict.Pending),
                Check("test", PrChecksVerdict.Passed),
                Check("pack", PrChecksVerdict.Failed)
            )
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: PrChecksVerdict.Failed, actual: status.Checks.Verdict);
        Assert.Equal(expected: ["build", "pack"], actual: status.Checks.Failing);
        Assert.Equal(expected: ["lint"], actual: status.Checks.Pending);
    }

    [Fact]
    public async Task PendingChecksAreNamedAsync()
    {
        this.GivenChecks(
            Checks(
                PrChecksVerdict.Pending,
                SHA,
                Check("build", PrChecksVerdict.Pending),
                Check("lint", PrChecksVerdict.Passed)
            )
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: PrChecksVerdict.Pending, actual: status.Checks.Verdict);
        Assert.Empty(status.Checks.Failing);
        Assert.Equal(expected: ["build"], actual: status.Checks.Pending);
    }

    [Fact]
    public async Task NoChecksIsReportedAsync()
    {
        this.GivenChecks(Checks(verdict: PrChecksVerdict.NoChecks, sha: SHA));

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: PrChecksVerdict.NoChecks, actual: status.Checks.Verdict);
        Assert.Empty(status.Checks.Failing);
        Assert.Empty(status.Checks.Pending);
    }

    [Fact]
    public async Task ADraftIsReadyFromWhenItWasCreatedAsync()
    {
        this.GivenView(View(draft: "yes"));
        this.GivenReadyAt(new(ReadyAt: "2026-01-01T00:00:00Z", Source: PrReadyAtService.SOURCE_CREATED));

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: "yes", actual: status.Draft);
        Assert.Equal(expected: "2026-01-01T00:00:00Z", actual: status.ReadyAt);
        Assert.Equal(expected: PrReadyAtService.SOURCE_CREATED, actual: status.ReadySource);
    }

    [Fact]
    public async Task ARepositoryWithoutABoardHasNoBoardStateAsync()
    {
        this.GivenBoardFails(new WorkflowBoardNotFoundException("no board"));

        PrStatusResponse status = await this.ReadAsync();

        Assert.Null(status.Board);
    }

    [Theory]
    [InlineData(WorkflowStatusCheckOutcome.NotOnBoard)]
    [InlineData(WorkflowStatusCheckOutcome.Unset)]
    public async Task APullRequestWithoutABoardStateHasNoBoardStateAsync(WorkflowStatusCheckOutcome outcome)
    {
        this.GivenBoard(new(Outcome: outcome, State: null, Message: "not on the board"));

        PrStatusResponse status = await this.ReadAsync();

        Assert.Null(status.Board);
    }

    [Fact]
    public async Task AnyOtherBoardFailureFailsTheStatusAsync()
    {
        this.GivenBoardFails(new BoardReadFailedException("could not read the board"));

        await Assert.ThrowsAsync<BoardReadFailedException>(this.ReadAsync);

        await this.AssertNotReadAsync(comments: true, readyAt: true);
    }

    [Fact]
    public async Task OnlyActivityAfterTheReadyTimeIsCountedAsync()
    {
        this.GivenComments(
            Comments(
                comments:
                [
                    Comment(author: "before", createdAt: "2026-01-01T00:00:00Z"),
                    Comment(author: "at-ready", createdAt: READY_AT),
                    Comment(author: "after", createdAt: "2026-01-03T00:00:00Z"),
                ],
                reviews:
                [
                    Review(author: "reviewer", state: "COMMENTED", submittedAt: "2026-01-04T00:00:00Z"),
                    Review(author: "drafting", state: "PENDING", submittedAt: string.Empty),
                    Review(author: "early", state: "APPROVED", submittedAt: "2026-01-01T12:00:00Z"),
                ]
            )
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(
            expected: new PrStatusActivity(
                Comments: 3,
                Reviews: 3,
                Threads: 5,
                Unresolved: 2,
                SinceReady: 2,
                Last: new PrStatusLastActivity(At: "2026-01-04T00:00:00Z", Author: "reviewer")
            ),
            actual: status.Activity
        );
    }

    [Fact]
    public async Task APendingReviewWithATimeIsNotCountedAsync()
    {
        this.GivenComments(
            Comments(
                comments: [],
                reviews: [Review(author: "drafting", state: "PENDING", submittedAt: "2026-01-04T00:00:00Z")]
            )
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: 0, actual: status.Activity.SinceReady);
        Assert.Null(status.Activity.Last);
    }

    [Fact]
    public async Task OfTwoActivitiesAtTheSameTimeTheLaterInTheListIsLastAsync()
    {
        this.GivenComments(
            Comments(
                comments: [Comment(author: "commenter", createdAt: "2026-01-03T00:00:00Z")],
                reviews: [Review(author: "reviewer", state: "APPROVED", submittedAt: "2026-01-03T00:00:00Z")]
            )
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(
            expected: new PrStatusLastActivity(At: "2026-01-03T00:00:00Z", Author: "reviewer"),
            actual: status.Activity.Last
        );
    }

    [Fact]
    public async Task ActivityBeforeReadyIsStillTheLastActivityAsync()
    {
        this.GivenComments(
            Comments(comments: [Comment(author: "early", createdAt: "2026-01-01T00:00:00Z")], reviews: [])
        );

        PrStatusResponse status = await this.ReadAsync();

        Assert.Equal(expected: 0, actual: status.Activity.SinceReady);
        Assert.Equal(
            expected: new PrStatusLastActivity(At: "2026-01-01T00:00:00Z", Author: "early"),
            actual: status.Activity.Last
        );
    }

    [Fact]
    public async Task AnUnreadableActivityTimeFailsTheStatusAsync()
    {
        this.GivenComments(Comments(comments: [Comment(author: "someone", createdAt: "yesterday")], reviews: []));

        PullRequestCommentsIncompleteException exception =
            await Assert.ThrowsAsync<PullRequestCommentsIncompleteException>(this.ReadAsync);

        Assert.Equal(expected: "could not read all the comments of " + REFERENCE, actual: exception.Message);
    }

    [Fact]
    public Task AnUnreadableReadyTimeFailsTheStatusAsync()
    {
        this.GivenReadyAt(new(ReadyAt: "yesterday", Source: PrReadyAtService.SOURCE_CREATED));

        return Assert.ThrowsAsync<PrReadyAtFailedException>(this.ReadAsync);
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundBeforeAnythingElseIsReadAsync()
    {
        this._view.ViewAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                fields: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new PullRequestNotFoundException("pull request " + REFERENCE + " was not found"));

        await Assert.ThrowsAsync<PullRequestNotFoundException>(this.ReadAsync);

        await this.AssertNothingButTheViewReadAsync();
    }

    [Fact]
    public async Task InvalidInputIsRefusedByTheViewAsync()
    {
        this._view.ViewAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                fields: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(ParameterValidationException.For(parameterName: "pullRequest", rule: "must be above 0"));

        await Assert.ThrowsAsync<ParameterValidationException>(this.ReadAsync);

        await this.AssertNotReadAsync(comments: true, readyAt: true);
    }

    [Fact]
    public async Task AHeadThatMovedBetweenReadsFailsTheStatusAsync()
    {
        this.GivenChecks(Checks(verdict: PrChecksVerdict.Passed, sha: OTHER_SHA));

        PrStatusHeadChangedException exception = await Assert.ThrowsAsync<PrStatusHeadChangedException>(this.ReadAsync);

        Assert.Equal(
            expected: "the head of pull request " + REFERENCE + " changed while its status was being read",
            actual: exception.Message
        );
        await this.AssertNotReadAsync(comments: true, readyAt: true);
    }

    [Fact]
    public Task AMissingViewFieldFailsTheStatusAsync()
    {
        this.GivenView(View(changedFiles: null));

        return Assert.ThrowsAsync<PullRequestViewFailedException>(this.ReadAsync);
    }

    [Fact]
    public Task AMissingHeadFailsTheStatusAsync()
    {
        this.GivenView(View(headSha: null));

        return Assert.ThrowsAsync<PullRequestViewFailedException>(this.ReadAsync);
    }

    private static bool AreTheStatusFields(IReadOnlyList<string>? fields)
    {
        return fields is not null
            && fields.Count == 15
            && fields.Contains(value: PullRequestViewFields.ChangedFiles, comparer: StringComparer.Ordinal)
            && !fields.Contains(value: PullRequestViewFields.CommitAuthors, comparer: StringComparer.Ordinal)
            && !fields.Contains(value: PullRequestViewFields.Files, comparer: StringComparer.Ordinal)
            && !fields.Contains(value: PullRequestViewFields.Body, comparer: StringComparer.Ordinal);
    }

    private async Task AssertNothingButTheViewReadAsync()
    {
        await this
            ._checks.DidNotReceiveWithAnyArgs()
            .ReadAsync(
                repository: default!,
                pullRequest: default,
                sha: default,
                branch: default,
                all: default,
                cancellationToken: default
            );
        await this
            ._statusCheck.DidNotReceiveWithAnyArgs()
            .CheckAsync(repository: default!, pullRequest: default, issue: default, cancellationToken: default);
        await this.AssertNotReadAsync(comments: true, readyAt: true);
    }

    private async Task AssertNotReadAsync(bool comments, bool readyAt)
    {
        if (comments)
        {
            await this
                ._comments.DidNotReceiveWithAnyArgs()
                .ReadAsync(repository: default!, pullRequest: default, cancellationToken: default);
        }

        if (readyAt)
        {
            await this
                ._readyAt.DidNotReceiveWithAnyArgs()
                .ReadAsync(repository: default!, pullRequest: default, cancellationToken: default);
        }
    }
}
