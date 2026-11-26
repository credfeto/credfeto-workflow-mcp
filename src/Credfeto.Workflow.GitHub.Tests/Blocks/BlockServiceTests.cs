using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Blocks;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Blocks;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection. The real leak checker is used, so each refusal is the one the rules give
public sealed class BlockServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int NUMBER = 174;
    private const string REFERENCE = REPOSITORY + "#174";
    private const string BODY = "Which image should this use?";
    private const string SECRET = "gh" + "p_abcdef";
    private const string COMMENT_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string MARKER = "<!-- orchestrator:env-block image-sha=" + SHA + " -->";
    private const string ISSUE_JSON = "{\"labels\":[{\"name\":\"AI-Work\"}]}";
    private const string BLOCKED_ISSUE_JSON = "{\"labels\":[{\"name\":\"AI-Work\"},{\"name\":\"blocked\"}]}";
    private const string PULL_REQUEST_JSON =
        "{\"labels\":[],\"pull_request\":{\"url\":\"https://api.github.com/repos/" + REPOSITORY + "/pulls/174\"}}";

    private static readonly Uri CommentUri = new(uriString: COMMENT_URL, uriKind: UriKind.Absolute);

    private static readonly Uri ItemUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/174");

    private readonly ICommentAddService _commentAddService;
    private readonly ILabelService _labelService;
    private readonly IRepositoryVisibilityLookup _visibilityLookup;

    public BlockServiceTests()
    {
        this._visibilityLookup = GetSubstitute<IRepositoryVisibilityLookup>();
        this._commentAddService = GetSubstitute<ICommentAddService>();
        this._labelService = GetSubstitute<ILabelService>();
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

    private static RecordingHttpMessageHandler ReadingItem(string json)
    {
        return Reading(() => Responses.Json(json));
    }

    private BlockService CreateService(RecordingHttpMessageHandler upstream)
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
            labelService: this._labelService
        );
    }

    private ValueTask<BlockResult> BlockIssueAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int issue = NUMBER,
        string body = BODY
    )
    {
        return this.CreateService(upstream)
            .BlockIssueAsync(
                repository: repository,
                issue: issue,
                body: body,
                cancellationToken: this.CancellationToken()
            );
    }

    private ValueTask<BlockResult> BlockPullRequestAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = NUMBER,
        string body = BODY
    )
    {
        return this.CreateService(upstream)
            .BlockPullRequestAsync(
                repository: repository,
                pullRequest: pullRequest,
                body: body,
                cancellationToken: this.CancellationToken()
            );
    }

    private ValueTask<CommentAddResult> CommentCallAsync(bool isPullRequest, string body = BODY)
    {
        return this._commentAddService.AddAsync(
            repository: REPOSITORY,
            pullRequest: isPullRequest ? NUMBER : null,
            issue: isPullRequest ? null : NUMBER,
            body: body,
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private void CommentReturns(bool isPullRequest, CommentAddResult result, string body = BODY)
    {
        this.CommentCallAsync(isPullRequest: isPullRequest, body: body).Returns(result);
    }

    private void CommentIsPosted(bool isPullRequest, string body = BODY)
    {
        this.CommentReturns(
            isPullRequest: isPullRequest,
            result: new(
                Outcome: CommentAddOutcome.Posted,
                Message: "the comment was posted on " + REFERENCE,
                Url: CommentUri,
                Rule: null,
                LineNumber: null
            ),
            body: body
        );
    }

    private ValueTask<LabelChangeResponse> BlockedCallAsync(bool isPullRequest)
    {
        return this._labelService.AddAsync(
            repository: REPOSITORY,
            pullRequest: isPullRequest ? NUMBER : null,
            issue: isPullRequest ? null : NUMBER,
            labels: Arg.Is<IReadOnlyList<string>>(labels => labels.Count == 1 && labels[0] == StandardLabels.BLOCKED),
            cancellationToken: Arg.Any<CancellationToken>()
        );
    }

    private void BlockedIsAdded(bool isPullRequest)
    {
        this.BlockedCallAsync(isPullRequest)
            .Returns(new LabelChangeResponse(Repository: REPOSITORY, Number: NUMBER, Labels: ["Blocked"]));
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
        await this.NoLabelWasWrittenAsync();
    }

    private async Task NoLabelWasWrittenAsync()
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
    }

    private Task AssertRefusedBeforeAnyCallAsync(
        RecordingHttpMessageHandler upstream,
        BlockResult result,
        string rule,
        int lineNumber
    )
    {
        Assert.Equal(expected: BlockOutcome.Refused, actual: result.Outcome);
        Assert.Equal(
            expected: "body refused: " + rule + " (line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ")",
            actual: result.Message
        );
        Assert.Equal(expected: rule, actual: result.Rule);
        Assert.Equal(expected: lineNumber, actual: result.LineNumber);
        Assert.Null(result.CommentUrl);
        Assert.Empty(upstream.Requests);

        return this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AnIssueIsCommentedOnThenBlockedAsync()
    {
        this.CommentIsPosted(isPullRequest: false);
        this.BlockedIsAdded(isPullRequest: false);
        using RecordingHttpMessageHandler upstream = ReadingItem(ISSUE_JSON);

        BlockResult result = await this.BlockIssueAsync(upstream);

        Assert.Equal(
            expected: new BlockResult(
                Outcome: BlockOutcome.Blocked,
                Message: "the comment was posted on " + REFERENCE + " and Blocked was added",
                CommentUrl: CommentUri,
                AlreadyBlocked: false,
                Rule: null,
                LineNumber: null
            ),
            actual: result
        );
        Assert.Equal(expected: ItemUri, actual: Assert.Single(upstream.Requests).RequestUri);

        Received.InOrder(() =>
        {
            _ = this.CommentCallAsync(isPullRequest: false).AsTask();
            _ = this.BlockedCallAsync(isPullRequest: false).AsTask();
        });
    }

    [Fact]
    public async Task APullRequestIsCommentedOnThenBlockedAsync()
    {
        this.CommentIsPosted(isPullRequest: true);
        this.BlockedIsAdded(isPullRequest: true);
        using RecordingHttpMessageHandler upstream = ReadingItem(PULL_REQUEST_JSON);

        BlockResult result = await this.BlockPullRequestAsync(upstream);

        Assert.Equal(expected: BlockOutcome.Blocked, actual: result.Outcome);
        Assert.Equal(expected: CommentUri, actual: result.CommentUrl);
        Assert.False(result.AlreadyBlocked, userMessage: "the pull request was not Blocked before");
        Assert.Equal(expected: ItemUri, actual: Assert.Single(upstream.Requests).RequestUri);

        Received.InOrder(() =>
        {
            _ = this.CommentCallAsync(isPullRequest: true).AsTask();
            _ = this.BlockedCallAsync(isPullRequest: true).AsTask();
        });
    }

    [Fact]
    public async Task AnItemAlreadyBlockedStillGetsTheCommentAsync()
    {
        this.CommentIsPosted(isPullRequest: false);
        this.BlockedIsAdded(isPullRequest: false);
        using RecordingHttpMessageHandler upstream = ReadingItem(BLOCKED_ISSUE_JSON);

        BlockResult result = await this.BlockIssueAsync(upstream);

        Assert.Equal(expected: BlockOutcome.Blocked, actual: result.Outcome);
        Assert.Equal(
            expected: "the comment was posted on " + REFERENCE + ", which was already Blocked",
            actual: result.Message
        );
        Assert.Equal(expected: CommentUri, actual: result.CommentUrl);
        Assert.True(result.AlreadyBlocked, userMessage: "the issue was already Blocked");

        Received.InOrder(() =>
        {
            _ = this.CommentCallAsync(isPullRequest: false).AsTask();
            _ = this.BlockedCallAsync(isPullRequest: false).AsTask();
        });
    }

    [Fact]
    public async Task ALeakIsRefusedWithItsLineBeforeAnythingIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockIssueAsync(upstream: upstream, body: BODY + "\ntoken " + SECRET);

        await this.AssertRefusedBeforeAnyCallAsync(upstream: upstream, result: result, rule: "token", lineNumber: 2);
    }

    [Fact]
    public async Task AnApprovalKeywordIsRefusedWithItsLineBeforeAnythingIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockPullRequestAsync(upstream: upstream, body: BODY + "\n\nLGTM");

        await this.AssertRefusedBeforeAnyCallAsync(
            upstream: upstream,
            result: result,
            rule: "approval keyword",
            lineNumber: 3
        );
    }

    [Fact]
    public async Task TheLeakCheckIsAppliedBeforeTheApprovalKeywordAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockIssueAsync(upstream: upstream, body: "Approved\ntoken " + SECRET);

        await this.AssertRefusedBeforeAnyCallAsync(upstream: upstream, result: result, rule: "token", lineNumber: 2);
    }

    [Fact]
    public async Task TheApprovalKeywordIsAppliedBeforeTheMarkerRuleAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockIssueAsync(upstream: upstream, body: MARKER + "\nApproved");

        await this.AssertRefusedBeforeAnyCallAsync(
            upstream: upstream,
            result: result,
            rule: "approval keyword",
            lineNumber: 2
        );
    }

    [Theory]
    [InlineData(MARKER)]
    [InlineData("<!-- ORCHESTRATOR:ENV-BLOCK -->")]
    [InlineData("see orchestrator:env-block")]
    public async Task AnyEnvBlockMarkerOnAnIssueIsRefusedAsync(string marker)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockIssueAsync(upstream: upstream, body: BODY + "\n" + marker);

        await this.AssertRefusedBeforeAnyCallAsync(
            upstream: upstream,
            result: result,
            rule: "env-block marker on an issue",
            lineNumber: 2
        );
    }

    [Theory]
    [InlineData("<!-- orchestrator:env-block -->")]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789ABCDEF0123456789abcdef01234567 -->")]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef0123456 -->")]
    [InlineData("<!-- orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef012345678 -->")]
    [InlineData("<!-- Orchestrator:Env-Block image-sha=0123456789abcdef0123456789abcdef01234567 -->")]
    [InlineData("orchestrator:env-block image-sha=0123456789abcdef0123456789abcdef01234567")]
    [InlineData(MARKER + " orchestrator:env-block")]
    public async Task AMalformedEnvBlockMarkerOnAPullRequestIsRefusedAsync(string marker)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        BlockResult result = await this.BlockPullRequestAsync(upstream: upstream, body: BODY + "\n" + marker);

        await this.AssertRefusedBeforeAnyCallAsync(
            upstream: upstream,
            result: result,
            rule: "malformed env-block marker",
            lineNumber: 2
        );
    }

    [Fact]
    public async Task ExactEnvBlockMarkersOnAPullRequestArePostedAsync()
    {
        const string body = BODY + "\n" + MARKER + "\n" + MARKER;
        this.CommentIsPosted(isPullRequest: true, body: body);
        this.BlockedIsAdded(isPullRequest: true);
        using RecordingHttpMessageHandler upstream = ReadingItem(PULL_REQUEST_JSON);

        BlockResult result = await this.BlockPullRequestAsync(upstream: upstream, body: body);

        Assert.Equal(expected: BlockOutcome.Blocked, actual: result.Outcome);
        Assert.Equal(expected: CommentUri, actual: result.CommentUrl);
    }

    [Fact]
    public async Task APullRequestNumberIsNotFoundAsAnIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadingItem(PULL_REQUEST_JSON);

        BlockResult result = await this.BlockIssueAsync(upstream);

        Assert.Equal(expected: BlockOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(expected: "issue " + REFERENCE + " was not found", actual: result.Message);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AnIssueNumberIsNotFoundAsAPullRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadingItem(ISSUE_JSON);

        BlockResult result = await this.BlockPullRequestAsync(upstream);

        Assert.Equal(expected: BlockOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(expected: "pull request " + REFERENCE + " was not found", actual: result.Message);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AMissingItemIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Reading(() =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.NotFound, json: "{\"message\":\"Not Found\"}")
        );

        BlockResult result = await this.BlockPullRequestAsync(upstream);

        Assert.Equal(expected: BlockOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(expected: "pull request " + REFERENCE + " was not found", actual: result.Message);
        Assert.Null(result.CommentUrl);
        await this.NothingWasWrittenAsync();
    }

    [Fact]
    public async Task AServerErrorReadingTheItemPostsNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        BlockResult result = await this.BlockIssueAsync(upstream);

        Assert.Equal(expected: BlockOutcome.NotPosted, actual: result.Outcome);
        Assert.StartsWith(
            expectedStartString: "could not read issue " + REFERENCE + ", so the comment was not posted: ",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData(CommentAddOutcome.NotPosted, BlockOutcome.NotPosted)]
    [InlineData(CommentAddOutcome.MayHaveBeenPosted, BlockOutcome.MayHaveBeenPosted)]
    [InlineData(CommentAddOutcome.Posted, BlockOutcome.MayHaveBeenPosted)]
    public async Task ACommentThatWasNotPostedAddsNoLabelAsync(CommentAddOutcome outcome, BlockOutcome expected)
    {
        const string message = "could not post the comment on " + REFERENCE;
        this.CommentReturns(
            isPullRequest: false,
            result: new(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null)
        );
        using RecordingHttpMessageHandler upstream = ReadingItem(ISSUE_JSON);

        BlockResult result = await this.BlockIssueAsync(upstream);

        Assert.Equal(
            expected: new BlockResult(
                Outcome: expected,
                Message: message,
                CommentUrl: null,
                AlreadyBlocked: false,
                Rule: null,
                LineNumber: null
            ),
            actual: result
        );
        await this.NoLabelWasWrittenAsync();
    }

    [Fact]
    public async Task ACommentRefusedByCommentAddKeepsItsRuleAsync()
    {
        this.CommentReturns(
            isPullRequest: true,
            result: new(
                Outcome: CommentAddOutcome.Refused,
                Message: "body refused: approval keyword (line 3)",
                Url: null,
                Rule: "approval keyword",
                LineNumber: 3
            )
        );
        using RecordingHttpMessageHandler upstream = ReadingItem(PULL_REQUEST_JSON);

        BlockResult result = await this.BlockPullRequestAsync(upstream);

        Assert.Equal(expected: BlockOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "body refused: approval keyword (line 3)", actual: result.Message);
        Assert.Equal(expected: "approval keyword", actual: result.Rule);
        Assert.Equal(expected: 3, actual: result.LineNumber);
        await this.NoLabelWasWrittenAsync();
    }

    [Fact]
    public async Task ALabelFailureNamesTheCommentPostedAsync()
    {
        this.CommentIsPosted(isPullRequest: true);
        this.BlockedCallAsync(isPullRequest: true)
            .ThrowsAsync(new LabelChangeFailedException("could not add labels to " + REFERENCE));
        using RecordingHttpMessageHandler upstream = ReadingItem(PULL_REQUEST_JSON);

        BlockResult result = await this.BlockPullRequestAsync(upstream);

        Assert.Equal(
            expected: new BlockResult(
                Outcome: BlockOutcome.LabelNotAdded,
                Message: "could not add the Blocked label to "
                    + REFERENCE
                    + "; steps done: comment posted "
                    + COMMENT_URL,
                CommentUrl: CommentUri,
                AlreadyBlocked: false,
                Rule: null,
                LineNumber: null
            ),
            actual: result
        );
    }

    [Fact]
    public async Task ACancellationAfterTheCommentIsNotReportedAsALabelFailureAsync()
    {
        this.CommentIsPosted(isPullRequest: false);
        this.BlockedCallAsync(isPullRequest: false).ThrowsAsync(new OperationCanceledException());
        using RecordingHttpMessageHandler upstream = ReadingItem(ISSUE_JSON);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await this.BlockIssueAsync(upstream));
    }

    [Theory]
    [InlineData("", 174, "repository")]
    [InlineData(REPOSITORY, 0, "issue")]
    [InlineData(REPOSITORY, -1, "issue")]
    public async Task InvalidIssueArgumentsAreRefusedBeforeAnyCallAsync(
        string repository,
        int issue,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(async () =>
            await this.BlockIssueAsync(upstream: upstream, repository: repository, issue: issue)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
        await this.NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData("", 174, "repository")]
    [InlineData(REPOSITORY, 0, "pr")]
    public async Task InvalidPullRequestArgumentsAreRefusedBeforeAnyCallAsync(
        string repository,
        int pullRequest,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(async () =>
            await this.BlockPullRequestAsync(upstream: upstream, repository: repository, pullRequest: pullRequest)
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
            await this.BlockIssueAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Equal(expected: "the body is empty", actual: exception.Message);
        Assert.Empty(upstream.Requests);
        await this.NothingWasWrittenAsync();
    }
}
