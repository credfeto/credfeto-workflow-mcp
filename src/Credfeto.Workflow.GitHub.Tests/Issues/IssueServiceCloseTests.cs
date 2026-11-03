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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueServiceCloseTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 16;
    private const string REFERENCE = "credfeto/credfeto-workflow-mcp#16";
    private const string COMMENT = "Done in a later release";

    private const string OPEN_ISSUE = """
        {"title":"Add issue close","state":"open","labels":[{"name":"feature"}],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/16"}
        """;

    private const string CLOSED_ISSUE = """
        {"title":"Add issue close","state":"closed","labels":[{"name":"feature"}],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/16"}
        """;

    private const string ON_HOLD_ISSUE = """
        {"title":"Add issue close","state":"open","labels":[{"name":"On Hold"}],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/16"}
        """;

    private const string PULL_REQUEST = """
        {"title":"Add issue close","state":"open","labels":[],"assignees":[],"body":"Body",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/16"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/16"}
        """;

    private const string EMPTY_TIMELINE = "[]";

    private const string CLOSING_PULL_REQUEST_TIMELINE = """
        [{"event":"cross-referenced","source":{"type":"issue","issue":{"number":20,"state":"open","body":"Closes #16",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/20"},
        "repository_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp"}}}]
        """;

    private const string CLOSED_PULL_REQUEST_TIMELINE = """
        [{"event":"cross-referenced","source":{"type":"issue","issue":{"number":20,"state":"closed","body":"Closes #16",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/20"},
        "repository_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp"}}}]
        """;

    private const string NO_BODY_PULL_REQUEST_TIMELINE = """
        [{"event":"cross-referenced","source":{"type":"issue","issue":{"number":20,"state":"open","body":null,
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/20"},
        "repository_url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp"}}}]
        """;

    private const string NO_REPOSITORY_PULL_REQUEST_TIMELINE = """
        [{"event":"cross-referenced","source":{"type":"issue","issue":{"number":20,"state":"open","body":"Closes #16",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/20"}}}}]
        """;

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/16");

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage CloseFlow(int index, string timeline)
    {
        return index switch
        {
            0 => Responses.Json(OPEN_ISSUE),
            1 => Responses.Json(timeline),
            _ => Responses.Json(CLOSED_ISSUE),
        };
    }

    private IssueService CreateService(RecordingHttpMessageHandler upstream, ILeakChecker? leakChecker = null)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: leakChecker ?? GetSubstitute<ILeakChecker>());
    }

    private async Task<IssueCloseResponse> CloseAsync(
        RecordingHttpMessageHandler upstream,
        string reason,
        string? comment,
        ILeakChecker? leakChecker = null
    )
    {
        return await this.CreateService(upstream: upstream, leakChecker: leakChecker)
            .CloseAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                reason: reason,
                comment: comment,
                cancellationToken: this.CancellationToken()
            );
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("not_planned")]
    public async Task CloseWithCommentPostsCommentThenClosesAsync(string reason)
    {
        using RecordingHttpMessageHandler upstream = new(index => CloseFlow(index: index, timeline: EMPTY_TIMELINE));

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: reason, comment: COMMENT);

        Assert.Equal(expected: REFERENCE, actual: result.Issue);
        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: reason, actual: result.Reason);
        Assert.Equal(
            expected: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/issues/16"),
            actual: result.Url
        );

        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[2].Method);
        Assert.Equal(expected: new Uri(IssueUri + "/comments"), actual: upstream.Requests[2].RequestUri);
        Assert.Contains(
            expectedSubstring: COMMENT,
            actualString: upstream.Requests[2].Body,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[3].Method);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[3].RequestUri);
        Assert.Contains(
            expectedSubstring: "\"state\":\"closed\"",
            actualString: upstream.Requests[3].Body,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "\"state_reason\":\"" + reason + "\"",
            actualString: upstream.Requests[3].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CloseNotPlannedClosesAsNotPlannedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index => CloseFlow(index: index, timeline: EMPTY_TIMELINE));

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: "not-planned", comment: null);

        Assert.Equal(expected: "not_planned", actual: result.Reason);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Contains(
            expectedSubstring: "\"state_reason\":\"not_planned\"",
            actualString: upstream.Requests[2].Body,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CloseWithoutCommentDoesNotPostCommentAsync(string? comment)
    {
        using RecordingHttpMessageHandler upstream = new(index => CloseFlow(index: index, timeline: EMPTY_TIMELINE));

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: "completed", comment: comment);

        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[2].Method);
    }

    [Fact]
    public async Task CloseIgnoresClosedPullRequestReferenceAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            CloseFlow(index: index, timeline: CLOSED_PULL_REQUEST_TIMELINE)
        );

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: "completed", comment: null);

        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(NO_BODY_PULL_REQUEST_TIMELINE)]
    [InlineData(NO_REPOSITORY_PULL_REQUEST_TIMELINE)]
    public async Task CloseIgnoresOpenPullRequestThatDoesNotCloseIssueAsync(string timeline)
    {
        using RecordingHttpMessageHandler upstream = new(index => CloseFlow(index: index, timeline: timeline));

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: "completed", comment: null);

        Assert.Equal(expected: "closed", actual: result.Outcome);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CloseServerErrorReadingIssueReportsCloseFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        IssueCloseFailedException exception = await Assert.ThrowsAsync<IssueCloseFailedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseInvalidIssueResponseReportsCloseFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json("not json") : Unexpected(index)
        );

        IssueCloseFailedException exception = await Assert.ThrowsAsync<IssueCloseFailedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseAlreadyClosedIssueWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(CLOSED_ISSUE) : Unexpected(index)
        );

        IssueCloseResponse result = await this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT);

        Assert.Equal(expected: "already-closed", actual: result.Outcome);
        Assert.Equal(expected: REFERENCE, actual: result.Issue);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseMissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ClosePullRequestNumberIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(PULL_REQUEST) : Unexpected(index)
        );

        await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not-a-repository", ISSUE, "completed")]
    [InlineData(REPOSITORY, 0, "completed")]
    [InlineData(REPOSITORY, -1, "completed")]
    [InlineData(REPOSITORY, ISSUE, "reopened")]
    [InlineData(REPOSITORY, ISSUE, "")]
    public async Task CloseRejectsInvalidInputAsync(string repository, int issue, string reason)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .CloseAsync(
                    repository: repository,
                    issue: issue,
                    reason: reason,
                    comment: COMMENT,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CloseOnHoldIssueIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(ON_HOLD_ISSUE) : Unexpected(index)
        );

        await Assert.ThrowsAsync<IssueCloseRefusedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task CloseIssueWithOpenClosingPullRequestIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index <= 1 ? CloseFlow(index: index, timeline: CLOSING_PULL_REQUEST_TIMELINE) : Unexpected(index)
        );

        await Assert.ThrowsAsync<IssueCloseRefusedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CloseWithLeakingCommentIsRefusedAsync()
    {
        ILeakChecker leakChecker = GetSubstitute<ILeakChecker>();
        leakChecker
            .CheckBody(Arg.Any<string>())
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2));

        using RecordingHttpMessageHandler upstream = new(Unexpected);

        IssueCloseRefusedException exception = await Assert.ThrowsAsync<IssueCloseRefusedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT, leakChecker: leakChecker)
        );

        Assert.Equal(expected: 2, actual: exception.LineNumber);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CloseWithApprovalKeywordCommentIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        IssueCloseRefusedException exception = await Assert.ThrowsAsync<IssueCloseRefusedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: "Line one\nLGTM")
        );

        Assert.Equal(expected: 2, actual: exception.LineNumber);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CloseFailureAfterCommentReportsCloseFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(OPEN_ISSUE),
                1 => Responses.Json(EMPTY_TIMELINE),
                2 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"id":1}"""),
                _ => Responses.Status(HttpStatusCode.UnprocessableEntity),
            }
        );

        await Assert.ThrowsAsync<IssueCloseFailedException>(() =>
            this.CloseAsync(upstream: upstream, reason: "completed", comment: COMMENT)
        );

        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[2].Method);
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[3].Method);
    }
}
