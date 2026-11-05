using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Comments;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Comments;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection. The real leak checker is used, so each refusal is the one the rules give
public sealed class CommentAddServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int NUMBER = 31;
    private const string REFERENCE = REPOSITORY + "#31";
    private const string BODY = "## Summary\n\nThe comment text.";
    private const string SECRET = "gh" + "p_abcdef";

    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-99";
    private const string PULL_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/31#issuecomment-99";

    private const string UNEXPECTED_RESULT =
        "unexpected result for the comment, which may have been posted on " + REFERENCE;

    private static readonly Uri CommentsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/31/comments"
    );

    private readonly IRepositoryVisibilityLookup _visibilityLookup;

    public CommentAddServiceTests()
    {
        this._visibilityLookup = GetSubstitute<IRepositoryVisibilityLookup>();
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage Throw(string message)
    {
        throw new HttpRequestException(message);
    }

    private static RecordingHttpMessageHandler Posting(Func<HttpResponseMessage> post)
    {
        return new(index =>
            index switch
            {
                0 => post(),
                _ => Unexpected(index),
            }
        );
    }

    private static RecordingHttpMessageHandler PostedAt(string htmlUrl)
    {
        string json = "{\"id\":99,\"html_url\":\"" + htmlUrl + "\"}";

        return Posting(() => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: json));
    }

    private CommentAddService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        LeakChecker leakChecker = new(
            visibilityLookup: this._visibilityLookup,
            options: Options.Create(new WorkflowOptions { PrivateOwners = "secretorg" })
        );

        return new(restClient: rest, leakChecker: leakChecker);
    }

    private async Task<CommentAddResult> AddAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int? pullRequest = null,
        int? issue = NUMBER,
        string body = BODY
    )
    {
        return await this.CreateService(upstream)
            .AddAsync(
                repository: repository,
                pullRequest: pullRequest,
                issue: issue,
                body: body,
                cancellationToken: this.CancellationToken()
            );
    }

    private static void AssertPosted(CommentAddResult result, string url)
    {
        Assert.Equal(
            expected: new CommentAddResult(
                Outcome: CommentAddOutcome.Posted,
                Message: "the comment was posted on " + REFERENCE,
                Url: new(url),
                Rule: null,
                LineNumber: null
            ),
            actual: result
        );
    }

    private static void AssertSinglePost(RecordingHttpMessageHandler upstream)
    {
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: CommentsUri, actual: request.RequestUri);
        Assert.Contains(
            expectedSubstring: "The comment text.",
            actualString: request.Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    private static void AssertRefused(CommentAddResult result, string rule, int? line)
    {
        string lineText = line is { } number ? " (line " + number.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;

        Assert.Equal(
            expected: new CommentAddResult(
                Outcome: CommentAddOutcome.Refused,
                Message: "body refused: " + rule + lineText,
                Url: null,
                Rule: rule,
                LineNumber: line
            ),
            actual: result
        );
    }

    private static void AssertFailed(CommentAddResult result, CommentAddOutcome outcome, string message)
    {
        Assert.Equal(
            expected: new CommentAddResult(Outcome: outcome, Message: message, Url: null, Rule: null, LineNumber: null),
            actual: result
        );
    }

    [Fact]
    public async Task IssueCommentIsPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = PostedAt(ISSUE_URL);

        CommentAddResult result = await this.AddAsync(upstream);

        AssertPosted(result: result, url: ISSUE_URL);
        AssertSinglePost(upstream);
        await this.ReceivedNoLookupsAsync();
    }

    [Theory]
    [InlineData(PULL_URL)]
    [InlineData(ISSUE_URL)]
    public async Task PullRequestCommentIsPostedInEitherUrlFormAsync(string url)
    {
        using RecordingHttpMessageHandler upstream = PostedAt(url);

        CommentAddResult result = await this.AddAsync(upstream: upstream, pullRequest: NUMBER, issue: null);

        AssertPosted(result: result, url: url);
        AssertSinglePost(upstream);
    }

    [Theory]
    [InlineData("https://github.example.com:8443/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-1")]
    [InlineData("https://github.com/credfeto/renamed-repository/pull/31#issuecomment-123456789")]
    public async Task HostAndRepositoryOfTheCommentUrlAreNotComparedAsync(string url)
    {
        using RecordingHttpMessageHandler upstream = PostedAt(url);

        CommentAddResult result = await this.AddAsync(upstream);

        AssertPosted(result: result, url: url);
    }

    [Theory]
    [InlineData(31, 31)]
    [InlineData(null, null)]
    public async Task BothOrNeitherTargetIsRefusedWithoutCallingGitHubAsync(int? pullRequest, int? issue)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, pullRequest: pullRequest, issue: issue)
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Equal(expected: "exactly one of pr or issue must be given", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0, null, "pr")]
    [InlineData(-1, null, "pr")]
    [InlineData(null, 0, "issue")]
    [InlineData(null, -5, "issue")]
    public async Task NumberBelowOneIsRefusedWithoutCallingGitHubAsync(
        int? pullRequest,
        int? issue,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, pullRequest: pullRequest, issue: issue)
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/a/b")]
    public async Task InvalidRepositoryIsRefusedWithoutCallingGitHubAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\n\r\n")]
    [InlineData("\u200B\u00A0 \n\uFEFF")]
    public async Task BlankBodyIsRefusedWithoutCallingGitHubAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Equal(expected: "the body is empty", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
        await this.ReceivedNoLookupsAsync();
    }

    [Fact]
    public async Task LeakIsRefusedWithItsLineWithoutPostingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: "line one\ntoken " + SECRET);

        AssertRefused(result: result, rule: "token", line: 2);
        Assert.DoesNotContain(
            expectedSubstring: SECRET,
            actualString: result.ToString(),
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task LeakOfTheWholeBodyIsRefusedWithoutALineAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: new('a', count: 65537));

        AssertRefused(result: result, rule: "too long", line: null);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task PrivateRepositoryReferenceIsRefusedWithoutPostingAsync()
    {
        Configure(
            call: this._visibilityLookup.GetVisibilityAsync(
                repository: REPOSITORY,
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            visibility: RepositoryVisibility.PUBLIC
        );
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: "line one\nsee secretorg/hidden#3");

        AssertRefused(result: result, rule: "private repository", line: 2);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("Approved", 1)]
    [InlineData("Thanks.\n\nLGTM, merging once green", 3)]
    [InlineData("> quoted\n    > approved in a code block", 2)]
    [InlineData("_approved_", 1)]
    public async Task ApprovalKeywordIsRefusedWithItsLineWithoutPostingAsync(string body, int line)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: body);

        AssertRefused(result: result, rule: "approval keyword", line: line);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("> Approved\n\nThe comment text.")]
    [InlineData("   > lgtm\nThe comment text.")]
    [InlineData("The comment text. The disapproved path stays.")]
    public async Task ApprovalKeywordOnAQuoteLineOrInAWordIsPostedAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = PostedAt(ISSUE_URL);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: body);

        AssertPosted(result: result, url: ISSUE_URL);
        AssertSinglePost(upstream);
    }

    [Fact]
    public async Task LeakCheckRunsBeforeTheApprovalKeywordRefusalAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        CommentAddResult result = await this.AddAsync(upstream: upstream, body: "approved\nsee /tmp/x");

        AssertRefused(result: result, rule: "host path", line: 2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RejectedPostIsNotPostedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Posting(() => Responses.Status(statusCode));

        CommentAddResult result = await this.AddAsync(upstream);

        AssertFailed(
            result: result,
            outcome: CommentAddOutcome.NotPosted,
            message: "could not post the comment on "
                + REFERENCE
                + ": GitHub answered with status "
                + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        );
        AssertSinglePost(upstream);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FailedPostMayHaveBeenPostedAndIsNotRetriedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Posting(() => Responses.Status(statusCode));

        CommentAddResult result = await this.AddAsync(upstream);

        AssertMayHaveBeenPosted(
            result: result,
            reason: "GitHub answered with status " + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        );
        AssertSinglePost(upstream);
    }

    [Fact]
    public async Task PostRequestFailureMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Posting(static () => Throw("connection reset with secret detail"));

        CommentAddResult result = await this.AddAsync(upstream);

        AssertMayHaveBeenPosted(result: result, reason: "the request to GitHub failed");
        Assert.DoesNotContain(
            expectedSubstring: "secret detail",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task UnreadablePostAnswerMayHaveBeenPostedAsync()
    {
        using RecordingHttpMessageHandler upstream = Posting(static () => Responses.Json("not json"));

        CommentAddResult result = await this.AddAsync(upstream);

        AssertMayHaveBeenPosted(result: result, reason: "the answer from GitHub could not be read");
    }

    [Theory]
    [InlineData("""{"id":99}""")]
    [InlineData("""{"id":99,"html_url":null}""")]
    [InlineData("""{"id":99,"html_url":""}""")]
    [InlineData("""{"id":99,"html_url":"http://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-99"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/32#issuecomment-99"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pulls/31#issuecomment-99"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/31"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/31#discussion_r99"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/issues/31#issuecomment-99"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-99\n"}""")]
    [InlineData("""{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/0#issuecomment-99"}""")]
    [InlineData(
        """{"id":99,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/99999999999#issuecomment-99"}"""
    )]
    public async Task UnexpectedCommentUrlMayHaveBeenPostedAsync(string posted)
    {
        using RecordingHttpMessageHandler upstream = Posting(() =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: posted)
        );

        CommentAddResult result = await this.AddAsync(upstream);

        AssertFailed(result: result, outcome: CommentAddOutcome.MayHaveBeenPosted, message: UNEXPECTED_RESULT);
        AssertSinglePost(upstream);
    }

    private static void AssertMayHaveBeenPosted(CommentAddResult result, string reason)
    {
        AssertFailed(
            result: result,
            outcome: CommentAddOutcome.MayHaveBeenPosted,
            message: "could not post the comment on "
                + REFERENCE
                + ", which may have been posted ("
                + reason
                + "); read the comments before posting again"
        );
    }

    private static void Configure(ValueTask<RepositoryVisibility> call, RepositoryVisibility visibility)
    {
        _ = call.Returns(_ => ValueTask.FromResult(visibility));
    }

    private ValueTask<RepositoryVisibility> ReceivedNoLookupsAsync()
    {
        return this
            ._visibilityLookup.DidNotReceive()
            .GetVisibilityAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }
}
