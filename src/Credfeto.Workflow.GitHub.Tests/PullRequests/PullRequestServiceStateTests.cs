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

public sealed class PullRequestServiceStateTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;
    private const string REFERENCE = "credfeto/credfeto-workflow-mcp#41";
    private const string COMMENT = "Superseded by a later change";
    private const string HTML_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/41";

    private const string OPEN = """
        {"number":41,"state":"open","merged":false,"user":{"login":"credfeto"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/41"}
        """;

    private const string CLOSED = """
        {"number":41,"state":"closed","merged":false,"user":{"login":"credfeto"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/41"}
        """;

    private const string MERGED = """
        {"number":41,"state":"closed","merged":true,"user":{"login":"credfeto"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/41"}
        """;

    private static readonly Uri PullUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/41");

    private static readonly Uri CommentsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/41/comments"
    );

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static string Mutate(string source, string from, string to)
    {
        Assert.Contains(expectedSubstring: from, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: from, newValue: to, comparisonType: StringComparison.Ordinal);
    }

    private static string OtherAuthor(string json)
    {
        return Mutate(source: json, from: "\"login\":\"credfeto\"", to: "\"login\":\"someone-else\"");
    }

    private PullRequestService CreateService(RecordingHttpMessageHandler restUpstream, ILeakChecker? leakChecker = null)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(restUpstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(
            graphQlClient: GetSubstitute<IGitHubGraphQlClient>(),
            restClient: rest,
            leakChecker: leakChecker ?? GetSubstitute<ILeakChecker>()
        );
    }

    private async Task<PullRequestStateChangeResponse> CloseAsync(
        RecordingHttpMessageHandler upstream,
        string? comment,
        ILeakChecker? leakChecker = null
    )
    {
        return await this.CreateService(restUpstream: upstream, leakChecker: leakChecker)
            .CloseAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                comment: comment,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<PullRequestStateChangeResponse> ReopenAsync(
        RecordingHttpMessageHandler upstream,
        string? comment,
        ILeakChecker? leakChecker = null
    )
    {
        return await this.CreateService(restUpstream: upstream, leakChecker: leakChecker)
            .ReopenAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                comment: comment,
                cancellationToken: this.CancellationToken()
            );
    }

    private static Func<int, HttpResponseMessage> Flow(string read, string written)
    {
        return index =>
            index switch
            {
                0 => Responses.Json(read),
                1 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"id":1}"""),
                2 => Responses.Json(written),
                _ => Unexpected(index),
            };
    }

    private static Func<int, HttpResponseMessage> FlowWithoutComment(string read, string written)
    {
        return index =>
            index switch
            {
                0 => Responses.Json(read),
                1 => Responses.Json(written),
                _ => Unexpected(index),
            };
    }

    private static void AssertCommentThenPatch(RecordingHttpMessageHandler upstream, string state)
    {
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: PullUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: CommentsUri, actual: upstream.Requests[1].RequestUri);
        Assert.Contains(
            expectedSubstring: COMMENT,
            actualString: upstream.Requests[1].Body,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[2].Method);
        Assert.Equal(expected: PullUri, actual: upstream.Requests[2].RequestUri);
        Assert.Equal(expected: "{\"state\":\"" + state + "\"}", actual: upstream.Requests[2].Body);
    }

    [Fact]
    public async Task CloseWithCommentPostsCommentThenClosesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Flow(read: OPEN, written: CLOSED));

        PullRequestStateChangeResponse result = await this.CloseAsync(upstream: upstream, comment: COMMENT);

        Assert.Equal(expected: REFERENCE, actual: result.PullRequest);
        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: new Uri(HTML_URL), actual: result.Url);
        AssertCommentThenPatch(upstream: upstream, state: "closed");
    }

    [Fact]
    public async Task ReopenWithCommentPostsCommentThenReopensAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Flow(read: CLOSED, written: OPEN));

        PullRequestStateChangeResponse result = await this.ReopenAsync(upstream: upstream, comment: COMMENT);

        Assert.Equal(expected: REFERENCE, actual: result.PullRequest);
        Assert.Equal(expected: "open", actual: result.Outcome);
        Assert.Equal(expected: new Uri(HTML_URL), actual: result.Url);
        AssertCommentThenPatch(upstream: upstream, state: "open");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CloseWithoutCommentDoesNotPostCommentAsync(string? comment)
    {
        using RecordingHttpMessageHandler upstream = new(FlowWithoutComment(read: OPEN, written: CLOSED));

        PullRequestStateChangeResponse result = await this.CloseAsync(upstream: upstream, comment: comment);

        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[1].Method);
    }

    [Fact]
    public async Task ReopenWithoutCommentDoesNotPostCommentAsync()
    {
        using RecordingHttpMessageHandler upstream = new(FlowWithoutComment(read: CLOSED, written: OPEN));

        PullRequestStateChangeResponse result = await this.ReopenAsync(upstream: upstream, comment: null);

        Assert.Equal(expected: "open", actual: result.Outcome);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[1].Method);
    }

    [Fact]
    public async Task PullRequestOpenedByAnotherAccountIsClosedAndReopenedAsync()
    {
        using RecordingHttpMessageHandler closeUpstream = new(
            Flow(read: OtherAuthor(OPEN), written: OtherAuthor(CLOSED))
        );

        PullRequestStateChangeResponse closed = await this.CloseAsync(upstream: closeUpstream, comment: COMMENT);

        Assert.Equal(expected: "closed", actual: closed.Outcome);
        AssertCommentThenPatch(upstream: closeUpstream, state: "closed");

        using RecordingHttpMessageHandler reopenUpstream = new(
            Flow(read: OtherAuthor(CLOSED), written: OtherAuthor(OPEN))
        );

        PullRequestStateChangeResponse reopened = await this.ReopenAsync(upstream: reopenUpstream, comment: COMMENT);

        Assert.Equal(expected: "open", actual: reopened.Outcome);
        AssertCommentThenPatch(upstream: reopenUpstream, state: "open");
    }

    [Fact]
    public async Task CloseAlreadyClosedWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(CLOSED) : Unexpected(index)
        );

        PullRequestStateChangeResponse result = await this.CloseAsync(upstream: upstream, comment: COMMENT);

        Assert.Equal(expected: "already-closed", actual: result.Outcome);
        Assert.Equal(expected: REFERENCE, actual: result.PullRequest);
        Assert.Equal(expected: new Uri(HTML_URL), actual: result.Url);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ReopenAlreadyOpenWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(OPEN) : Unexpected(index)
        );

        PullRequestStateChangeResponse result = await this.ReopenAsync(upstream: upstream, comment: COMMENT);

        Assert.Equal(expected: "already-open", actual: result.Outcome);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseMergedIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(MERGED) : Unexpected(index)
        );

        PullRequestStateChangeRefusedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeRefusedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT)
            );

        Assert.Equal(expected: "a merged pull request cannot be closed or reopened", actual: exception.Rule);
        Assert.Equal(
            expected: "close of pull request credfeto/credfeto-workflow-mcp#41 was refused: a merged pull request cannot be closed or reopened",
            actual: exception.Message
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ReopenMergedIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(MERGED) : Unexpected(index)
        );

        PullRequestStateChangeRefusedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeRefusedException>(() =>
                this.ReopenAsync(upstream: upstream, comment: COMMENT)
            );

        Assert.Equal(expected: "a merged pull request cannot be closed or reopened", actual: exception.Rule);
        Assert.StartsWith(
            expectedStartString: "reopen of pull request",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseIssueNumberIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.CloseAsync(upstream: upstream, comment: COMMENT)
        );

        Assert.Equal(
            expected: "pull request credfeto/credfeto-workflow-mcp#41 was not found",
            actual: exception.Message
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ReopenIssueNumberIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReopenAsync(upstream: upstream, comment: COMMENT)
        );

        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not-a-repository", PULL_REQUEST)]
    [InlineData(REPOSITORY, 0)]
    [InlineData(REPOSITORY, -1)]
    public async Task CloseRejectsInvalidInputAsync(string repository, int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .CloseAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    comment: COMMENT,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("not-a-repository", PULL_REQUEST)]
    [InlineData(REPOSITORY, 0)]
    [InlineData(REPOSITORY, -1)]
    public async Task ReopenRejectsInvalidInputAsync(string repository, int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .ReopenAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    comment: COMMENT,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CloseWithLeakingCommentIsRefusedAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckBody(Arg.Any<string>())
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PullRequestStateChangeRefusedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeRefusedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT, leakChecker: leakChecker)
            );

        Assert.Equal(expected: "token", actual: exception.Rule);
        Assert.Equal(expected: 2, actual: exception.LineNumber);
        Assert.Equal(expected: "comment refused: token (line 2)", actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ReopenWithApprovalKeywordCommentIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PullRequestStateChangeRefusedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeRefusedException>(() =>
                this.ReopenAsync(upstream: upstream, comment: "Line one\nLGTM")
            );

        Assert.Equal(expected: "approval keyword", actual: exception.Rule);
        Assert.Equal(expected: 2, actual: exception.LineNumber);
        Assert.Equal(expected: "comment refused: approval keyword (line 2)", actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CloseServerErrorReadingReportsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT)
            );

        Assert.Equal(
            expected: "could not close pull request credfeto/credfeto-workflow-mcp#41",
            actual: exception.Message
        );
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("\"number\":41", "\"number\":42")]
    [InlineData("\"number\":41,", "")]
    [InlineData("\"state\":\"open\"", "\"state\":\"locked\"")]
    [InlineData("\"merged\":false", "\"merged\":null")]
    [InlineData(HTML_URL, "ftp://github.com/credfeto/credfeto-workflow-mcp/pull/41")]
    [InlineData(HTML_URL, "not a url")]
    [InlineData("\"html_url\":\"" + HTML_URL + "\"", "\"html_url\":null")]
    public async Task CloseInvalidReadReportsFailedAsync(string from, string to)
    {
        string read = Mutate(source: OPEN, from: from, to: to);
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(read) : Unexpected(index)
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT)
            );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseCommentFailureReportsCommentNotPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(OPEN),
                1 => Responses.Status(HttpStatusCode.UnprocessableEntity),
                _ => Unexpected(index),
            }
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT)
            );

        Assert.Equal(
            expected: "could not close pull request credfeto/credfeto-workflow-mcp#41",
            actual: exception.Message
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CloseFailureAfterCommentSaysCommentWasPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(OPEN),
                1 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"id":1}"""),
                _ => Responses.Status(HttpStatusCode.UnprocessableEntity),
            }
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.CloseAsync(upstream: upstream, comment: COMMENT)
            );

        Assert.Equal(
            expected: "could not close pull request credfeto/credfeto-workflow-mcp#41; the comment was posted",
            actual: exception.Message
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReopenFailureWithoutCommentDoesNotMentionCommentAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(CLOSED) : Responses.Status(HttpStatusCode.UnprocessableEntity)
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.ReopenAsync(upstream: upstream, comment: null)
            );

        Assert.Equal(
            expected: "could not reopen pull request credfeto/credfeto-workflow-mcp#41",
            actual: exception.Message
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReopenUnconfirmedStateReportsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(FlowWithoutComment(read: CLOSED, written: CLOSED));

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.ReopenAsync(upstream: upstream, comment: null)
            );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CloseInvalidWriteResponseReportsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            FlowWithoutComment(read: OPEN, written: Mutate(source: CLOSED, from: "\"number\":41", to: "\"number\":7"))
        );

        PullRequestStateChangeFailedException exception =
            await Assert.ThrowsAsync<PullRequestStateChangeFailedException>(() =>
                this.CloseAsync(upstream: upstream, comment: null)
            );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }
}
