using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrReviewCommentServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 87;
    private const string PATH = "src/Example/Program.cs";
    private const int LINE = 12;
    private const string BODY = "This branch is never reached";
    private const string HEAD_SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string PULL_REQUEST_JSON = """
        {"head":{"ref":"feat/branch","sha":"0123456789abcdef0123456789abcdef01234567"},"base":{"ref":"main","sha":"fedcba9876543210fedcba9876543210fedcba98"}}
        """;

    private const string POSTED = """
        {"id":456,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}
        """;

    private static readonly Uri PullRequestUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87");

    private static readonly Uri CommentsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87/comments"
    );

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage Throw(string message)
    {
        throw new HttpRequestException(message);
    }

    private static RecordingHttpMessageHandler Flow(Func<HttpResponseMessage> post)
    {
        return new(index =>
            index switch
            {
                0 => Responses.Json(PULL_REQUEST_JSON),
                1 => post(),
                _ => Unexpected(index),
            }
        );
    }

    private static RecordingHttpMessageHandler ReadOnly(Func<HttpResponseMessage> read)
    {
        return new(index =>
            index switch
            {
                0 => read(),
                _ => Unexpected(index),
            }
        );
    }

    private static HttpResponseMessage Created(string json)
    {
        return Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: json);
    }

    private PrReviewCommentService CreateService(RecordingHttpMessageHandler upstream, ILeakChecker? leakChecker = null)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: leakChecker ?? GetSubstitute<ILeakChecker>());
    }

    private async Task<PrReviewCommentResult> PostAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PULL_REQUEST,
        string path = PATH,
        int line = LINE,
        int? startLine = null,
        string? side = null,
        string body = BODY,
        ILeakChecker? leakChecker = null
    )
    {
        return await this.CreateService(upstream: upstream, leakChecker: leakChecker)
            .PostAsync(
                repository: repository,
                pullRequest: pullRequest,
                path: path,
                line: line,
                startLine: startLine,
                side: side,
                body: body,
                cancellationToken: this.CancellationToken()
            );
    }

    private static async Task AssertInvalidAsync(Func<RecordingHttpMessageHandler, Task<PrReviewCommentResult>> post)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() => post(upstream));

        Assert.Empty(upstream.Requests);
    }

    private static void AssertFailed(PrReviewCommentResult result, PrReviewCommentOutcome outcome, string message)
    {
        Assert.Equal(expected: outcome, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: message,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.CommentId);
        Assert.Null(result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
    }

    private static void AssertRefused(
        PrReviewCommentResult result,
        RecordingHttpMessageHandler upstream,
        string rule,
        int? lineNumber
    )
    {
        Assert.Equal(expected: PrReviewCommentOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "the review comment was refused", actual: result.Message);
        Assert.Equal(expected: rule, actual: result.Rule);
        Assert.Equal(expected: lineNumber, actual: result.LineNumber);
        Assert.Null(result.CommentId);
        Assert.Null(result.Url);
        Assert.Empty(upstream.Requests);
    }

    private static void AssertBodyContains(RecordingHttpMessageHandler upstream, string expected)
    {
        Assert.Contains(
            expectedSubstring: expected,
            actualString: upstream.Requests[1].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    private static void AssertBodyDoesNotContain(RecordingHttpMessageHandler upstream, string expected)
    {
        Assert.DoesNotContain(
            expectedSubstring: expected,
            actualString: upstream.Requests[1].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task SingleLineCommentIsPostedOnTheHeadCommitAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Created(POSTED));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        Assert.Equal(expected: PrReviewCommentOutcome.Posted, actual: result.Outcome);
        Assert.Equal(
            expected: "the review comment was posted on src/Example/Program.cs line 12 (RIGHT)",
            actual: result.Message
        );
        Assert.Equal(expected: 456L, actual: result.CommentId);
        Assert.Equal(
            expected: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"),
            actual: result.Url
        );
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: PullRequestUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: CommentsUri, actual: upstream.Requests[1].RequestUri);
        AssertBodyContains(upstream: upstream, expected: "\"body\":\"" + BODY + "\"");
        AssertBodyContains(upstream: upstream, expected: "\"commit_id\":\"" + HEAD_SHA + "\"");
        AssertBodyContains(upstream: upstream, expected: "\"path\":\"" + PATH + "\"");
        AssertBodyContains(upstream: upstream, expected: "\"line\":12");
        AssertBodyContains(upstream: upstream, expected: "\"side\":\"RIGHT\"");
        AssertBodyDoesNotContain(upstream: upstream, expected: "start_line");
        AssertBodyDoesNotContain(upstream: upstream, expected: "start_side");
    }

    [Theory]
    [InlineData("left")]
    [InlineData("LEFT")]
    public async Task SingleLineCommentOnTheOldVersionIsPostedOnTheLeftSideAsync(string side)
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Created(POSTED));

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, side: side);

        Assert.Equal(expected: PrReviewCommentOutcome.Posted, actual: result.Outcome);
        Assert.Equal(
            expected: "the review comment was posted on src/Example/Program.cs line 12 (LEFT)",
            actual: result.Message
        );
        AssertBodyContains(upstream: upstream, expected: "\"side\":\"LEFT\"");
        AssertBodyDoesNotContain(upstream: upstream, expected: "start_line");
    }

    [Theory]
    [InlineData("right", "RIGHT")]
    [InlineData("RIGHT", "RIGHT")]
    [InlineData("LEFT", "LEFT")]
    public async Task RangeCommentIsPostedWithItsFirstLineOnTheSameSideAsync(string side, string expectedSide)
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Created(POSTED));

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, startLine: 10, side: side);

        Assert.Equal(expected: PrReviewCommentOutcome.Posted, actual: result.Outcome);
        Assert.Equal(
            expected: "the review comment was posted on src/Example/Program.cs lines 10-12 (" + expectedSide + ")",
            actual: result.Message
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertBodyContains(upstream: upstream, expected: "\"line\":12");
        AssertBodyContains(upstream: upstream, expected: "\"side\":\"" + expectedSide + "\"");
        AssertBodyContains(upstream: upstream, expected: "\"start_line\":10");
        AssertBodyContains(upstream: upstream, expected: "\"start_side\":\"" + expectedSide + "\"");
    }

    [Fact]
    public async Task CommentUrlWithRepositoryInAnotherCaseIsPostedAsync()
    {
        const string posted = """
            {"id":456,"html_url":"https://github.com/Credfeto/Credfeto-Workflow-Mcp/pull/87#discussion_r456"}
            """;

        using RecordingHttpMessageHandler upstream = Flow(static () => Created(posted));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        Assert.Equal(expected: PrReviewCommentOutcome.Posted, actual: result.Outcome);
        Assert.Equal(expected: 456L, actual: result.CommentId);
    }

    [Theory]
    [InlineData("not-a-repository")]
    [InlineData("")]
    public Task InvalidRepositoryIsRefusedWithoutCallingGitHubAsync(string repository)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, repository: repository));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task InvalidPullRequestIsRefusedWithoutCallingGitHubAsync(int pullRequest)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, pullRequest: pullRequest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/src/Program.cs")]
    [InlineData("src\\Program.cs")]
    [InlineData("../secret.txt")]
    [InlineData("src/../../secret.txt")]
    [InlineData("src/..")]
    [InlineData("./src/Program.cs")]
    [InlineData("src//Program.cs")]
    [InlineData("src/")]
    [InlineData("src/Program.cs\n")]
    [InlineData("src/\u0007Program.cs")]
    public Task InvalidPathIsRefusedWithoutCallingGitHubAsync(string path)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, path: path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task InvalidLineIsRefusedWithoutCallingGitHubAsync(int line)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, line: line));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(LINE)]
    [InlineData(LINE + 1)]
    public Task InvalidStartLineIsRefusedWithoutCallingGitHubAsync(int startLine)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, startLine: startLine));
    }

    [Theory]
    [InlineData("")]
    [InlineData("BOTH")]
    [InlineData(" RIGHT")]
    public Task InvalidSideIsRefusedWithoutCallingGitHubAsync(string side)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, side: side));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public Task EmptyBodyIsRefusedWithoutCallingGitHubAsync(string body)
    {
        return AssertInvalidAsync(upstream => this.PostAsync(upstream: upstream, body: body));
    }

    [Fact]
    public async Task InvalidInputNamesTheParameterAndRuleAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.PostAsync(upstream: upstream, path: "../secret.txt")
        );

        Assert.Equal(expected: "path", actual: exception.ParameterName);
        Assert.Equal(expected: "it must not contain an empty, . or .. segment", actual: exception.Rule);
    }

    [Fact]
    public async Task LeakingBodyIsRefusedWithoutCallingGitHubAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckBody(Arg.Any<string>())
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, leakChecker: leakChecker);

        AssertRefused(result: result, upstream: upstream, rule: "the comment must not contain token", lineNumber: 2);
    }

    [Fact]
    public async Task BodyNamingAPrivateRepositoryIsRefusedWithoutCallingGitHubAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: 1));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, leakChecker: leakChecker);

        AssertRefused(
            result: result,
            upstream: upstream,
            rule: "the comment must not contain private repository",
            lineNumber: 1
        );
    }

    [Fact]
    public async Task LeakingPathIsRefusedWithoutCallingGitHubAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckValue(subject: LeakSubject.TEXT, value: PATH)
            .Returns(new LeakRefusal(Subject: LeakSubject.TEXT, Rule: LeakRule.HOST_PATH, LineNumber: null));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, leakChecker: leakChecker);

        AssertRefused(
            result: result,
            upstream: upstream,
            rule: "the path must not contain host path",
            lineNumber: null
        );
        leakChecker.DidNotReceive().CheckBody(Arg.Any<string>());
    }

    [Fact]
    public async Task ApprovalKeywordIsRefusedWithoutCallingGitHubAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrReviewCommentResult result = await this.PostAsync(upstream: upstream, body: "A note\nApproved");

        AssertRefused(
            result: result,
            upstream: upstream,
            rule: "the comment must not contain an approval keyword",
            lineNumber: 2
        );
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadOnly(static () => Responses.Status(HttpStatusCode.NotFound));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.NotFound,
            message: "the pull request credfeto/credfeto-workflow-mcp#87 was not found"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PullRequestReadFailureIsNotPostedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = ReadOnly(() => Responses.Status(statusCode));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.NotPosted,
            message: "the review comment was not posted: could not read the pull request"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"head":null}""")]
    [InlineData("""{"head":{"ref":"feat/branch"}}""")]
    [InlineData("""{"head":{"ref":"feat/branch","sha":""}}""")]
    public async Task UnreadablePullRequestIsNotPostedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = ReadOnly(() => Responses.Json(json));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.NotPosted,
            message: "the review comment was not posted: could not read the pull request"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task PullRequestReadRequestFailureIsNotPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = ReadOnly(static () => Throw("connection reset"));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.NotPosted,
            message: "the review comment was not posted: could not read the pull request"
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RejectedCommentIsNotPostedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Status(statusCode));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.NotPosted,
            message: "the review comment was not posted: GitHub answered with status "
                + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FailedCommentMayHaveBeenPostedAndIsNotRetriedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Status(statusCode));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.MayHaveBeenPosted,
            message: "GitHub answered with status " + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        );
        Assert.Contains(
            expectedSubstring: "use the pr_comments tool",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task UnreadableCommentAnswerMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Json("not json"));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.MayHaveBeenPosted,
            message: "the answer from GitHub could not be read"
        );
    }

    [Fact]
    public async Task CommentRequestFailureMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Throw("connection reset with secret detail"));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReviewCommentOutcome.MayHaveBeenPosted,
            message: "the request to GitHub failed"
        );
        Assert.DoesNotContain(
            expectedSubstring: "secret detail",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("""{"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}""")]
    [InlineData("""{"id":0,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r0"}""")]
    [InlineData("""{"id":456}""")]
    [InlineData("""{"id":456,"html_url":"/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}""")]
    [InlineData("""{"id":456,"html_url":"ftp://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}""")]
    [InlineData("""{"id":456,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87"}""")]
    [InlineData(
        """{"id":456,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/88#discussion_r456"}"""
    )]
    [InlineData(
        """{"id":456,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r457"}"""
    )]
    [InlineData("""{"id":456,"html_url":"https://github.com/credfeto/another-repository/pull/87#discussion_r456"}""")]
    public async Task UnexpectedCommentAnswerMayHaveBeenPostedAsync(string posted)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Created(posted));

        PrReviewCommentResult result = await this.PostAsync(upstream);

        AssertFailed(result: result, outcome: PrReviewCommentOutcome.MayHaveBeenPosted, message: "unexpected answer");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }
}
