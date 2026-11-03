using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
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

public sealed class PrReplyServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 87;
    private const string COMMENT_ID = "123";
    private const string REPLY = "Fixed in abc1234: the reply";

    private const string TOP_COMMENT = """
        {"id":123,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}
        """;

    private const string NESTED_COMMENT = """
        {"id":123,"in_reply_to_id":100,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}
        """;

    private const string POSTED = """
        {"id":456,"in_reply_to_id":123,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}
        """;

    private static readonly Uri CommentUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/comments/" + COMMENT_ID
    );

    private static readonly Uri RepliesUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87/comments/123/replies"
    );

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage Throw(string message)
    {
        throw new HttpRequestException(message);
    }

    private static RecordingHttpMessageHandler Flow(string comment, Func<HttpResponseMessage> post)
    {
        return new(index =>
            index switch
            {
                0 => Responses.Json(comment),
                1 => post(),
                _ => Unexpected(index),
            }
        );
    }

    private PrReplyService CreateService(RecordingHttpMessageHandler upstream, ILeakChecker? leakChecker = null)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: leakChecker ?? GetSubstitute<ILeakChecker>());
    }

    private async Task<PrReplyResult> ReplyAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PULL_REQUEST,
        string commentId = COMMENT_ID,
        string body = REPLY,
        ILeakChecker? leakChecker = null
    )
    {
        return await this.CreateService(upstream: upstream, leakChecker: leakChecker)
            .ReplyAsync(
                repository: repository,
                pullRequest: pullRequest,
                commentId: commentId,
                body: body,
                cancellationToken: this.CancellationToken()
            );
    }

    private static void AssertFailed(PrReplyResult result, PrReplyOutcome outcome, string message)
    {
        Assert.Equal(expected: outcome, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: message,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.CommentId);
        Assert.Null(result.InReplyToId);
        Assert.Null(result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
    }

    [Fact]
    public async Task ReplyToTopCommentPostsToItsThreadAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: static () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: POSTED)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        Assert.Equal(expected: PrReplyOutcome.Posted, actual: result.Outcome);
        Assert.Equal(expected: "the reply was posted to the thread of comment 123", actual: result.Message);
        Assert.Equal(expected: 456L, actual: result.CommentId);
        Assert.Equal(expected: 123L, actual: result.InReplyToId);
        Assert.Equal(
            expected: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"),
            actual: result.Url
        );
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: CommentUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: RepliesUri, actual: upstream.Requests[1].RequestUri);
        Assert.Contains(
            expectedSubstring: REPLY,
            actualString: upstream.Requests[1].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReplyToReplyPostsToTheThreadRootAsync()
    {
        const string posted = """
            {"id":456,"in_reply_to_id":100,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#discussion_r456"}
            """;

        using RecordingHttpMessageHandler upstream = Flow(
            comment: NESTED_COMMENT,
            post: static () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: posted)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        Assert.Equal(expected: PrReplyOutcome.Posted, actual: result.Outcome);
        Assert.Equal(expected: "the reply was posted to the thread of comment 100", actual: result.Message);
        Assert.Equal(expected: 100L, actual: result.InReplyToId);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87/comments/100/replies"),
            actual: upstream.Requests[1].RequestUri
        );
    }

    [Fact]
    public async Task ReplyUrlWithRepositoryInAnotherCaseIsPostedAsync()
    {
        const string posted = """
            {"id":456,"html_url":"https://github.com/Credfeto/Credfeto-Workflow-Mcp/pull/87#discussion_r456"}
            """;

        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: static () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: posted)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        Assert.Equal(expected: PrReplyOutcome.Posted, actual: result.Outcome);
        Assert.Equal(expected: 456L, actual: result.CommentId);
    }

    [Theory]
    [InlineData("not-a-repository")]
    [InlineData("")]
    public async Task InvalidRepositoryIsRefusedWithoutCallingGitHubAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReplyAsync(upstream: upstream, repository: repository)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidPullRequestIsRefusedWithoutCallingGitHubAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReplyAsync(upstream: upstream, pullRequest: pullRequest)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task InvalidCommentIdIsRefusedWithoutCallingGitHubAsync(string commentId)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReplyAsync(upstream: upstream, commentId: commentId)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyBodyIsRefusedWithoutCallingGitHubAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() => this.ReplyAsync(upstream: upstream, body: body));

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task LeakingBodyIsRefusedWithoutCallingGitHubAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckBody(Arg.Any<string>())
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrReplyResult result = await this.ReplyAsync(upstream: upstream, leakChecker: leakChecker);

        Assert.Equal(expected: PrReplyOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "the reply was refused", actual: result.Message);
        Assert.Contains(
            expectedSubstring: "TOKEN",
            actualString: result.Rule,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: 2, actual: result.LineNumber);
        Assert.Null(result.CommentId);
        Assert.Null(result.InReplyToId);
        Assert.Null(result.Url);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task MissingCommentIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotFound,
            message: "the comment 123 is not an inline review comment of credfeto/credfeto-workflow-mcp#87"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CommentReadFailureIsNotPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.UnprocessableEntity)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotPosted,
            message: "the reply was not posted: could not read the comment"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task UnreadableCommentIsNotPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("not json"),
                _ => Unexpected(index),
            }
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotPosted,
            message: "the reply was not posted: could not read the comment"
        );
    }

    [Theory]
    [InlineData(
        """{"id":124,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}"""
    )]
    [InlineData("""{"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}""")]
    [InlineData(
        """{"id":0,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}"""
    )]
    [InlineData(
        """{"id":123,"in_reply_to_id":0,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}"""
    )]
    [InlineData(
        """{"id":123,"in_reply_to_id":-1,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87"}"""
    )]
    public async Task MalformedCommentIsNotPostedAsync(string comment)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(comment),
                _ => Unexpected(index),
            }
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotPosted,
            message: "the reply was not posted: could not read the comment"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("""{"id":123}""")]
    [InlineData(
        """{"id":123,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/88"}"""
    )]
    [InlineData(
        """{"id":123,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/issues/87"}"""
    )]
    [InlineData(
        """{"id":123,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/87/"}"""
    )]
    [InlineData(
        """{"id":123,"pull_request_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/"}"""
    )]
    public async Task CommentOfAnotherPullRequestIsNotFoundAsync(string comment)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(comment),
                _ => Unexpected(index),
            }
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotFound,
            message: "the comment 123 is not an inline review comment of credfeto/credfeto-workflow-mcp#87"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RejectedReplyIsNotPostedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: () => Responses.Status(statusCode)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.NotPosted,
            message: "the reply was not posted: GitHub answered with status "
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
    public async Task FailedReplyMayHaveBeenPostedAndIsNotRetriedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: () => Responses.Status(statusCode)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.MayHaveBeenPosted,
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
    public async Task UnreadableReplyAnswerMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: static () => Responses.Json("not json")
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.MayHaveBeenPosted,
            message: "the answer from GitHub could not be read"
        );
    }

    [Fact]
    public async Task ReplyRequestFailureMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: static () => Throw("connection reset with secret detail")
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(
            result: result,
            outcome: PrReplyOutcome.MayHaveBeenPosted,
            message: "the request to GitHub failed"
        );
        Assert.DoesNotContain(
            expectedSubstring: "secret detail",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
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
    public async Task UnexpectedReplyAnswerMayHaveBeenPostedAsync(string posted)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            comment: TOP_COMMENT,
            post: () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: posted)
        );

        PrReplyResult result = await this.ReplyAsync(upstream);

        AssertFailed(result: result, outcome: PrReplyOutcome.MayHaveBeenPosted, message: "unexpected answer");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }
}
