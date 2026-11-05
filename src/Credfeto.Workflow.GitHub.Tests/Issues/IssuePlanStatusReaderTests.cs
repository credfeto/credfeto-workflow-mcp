using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssuePlanStatusReaderTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 17;
    private const string NOT_FOUND = "issue credfeto/credfeto-workflow-mcp#17 was not found";
    private const string FAILED = "could not read issue credfeto/credfeto-workflow-mcp#17";
    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/17";
    private const string CREATED = "2026-01-02T03:04:05Z";
    private const string PLAN = "Some intro\n## Implementation Plan\n1. Do it";

    private const string COMMENTS_URI =
        GitHubServices.ApiBaseUrl + "repos/credfeto/credfeto-workflow-mcp/issues/17/comments";

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/17");

    private static readonly Uri FirstCommentsUri = new(COMMENTS_URI + "?per_page=100");

    private static string Issue(string labels = "[]", string htmlUrl = ISSUE_URL, string pullRequest = "null")
    {
        return "{\"html_url\":\"" + htmlUrl + "\",\"labels\":" + labels + ",\"pull_request\":" + pullRequest + "}";
    }

    private static string Text(string value)
    {
        return "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
    }

    private static string CommentUrl(long id)
    {
        return ISSUE_URL + "#issuecomment-" + id.ToString(CultureInfo.InvariantCulture);
    }

    private static string Comment(
        long id,
        string body = "Hello",
        string login = "alice",
        string association = "OWNER",
        string createdAt = CREATED
    )
    {
        return RawComment(
            id: id,
            user: "{\"login\":" + Text(login) + ",\"type\":\"User\"}",
            association: Text(association),
            createdAt: Text(createdAt),
            htmlUrl: Text(CommentUrl(id)),
            body: Text(body)
        );
    }

    private static string RawComment(
        long id,
        string user = "{\"login\":\"alice\",\"type\":\"User\"}",
        string association = "\"OWNER\"",
        string createdAt = "\"" + CREATED + "\"",
        string? htmlUrl = null,
        string body = "\"Hello\""
    )
    {
        return "{\"id\":"
            + id.ToString(CultureInfo.InvariantCulture)
            + ",\"user\":"
            + user
            + ",\"author_association\":"
            + association
            + ",\"created_at\":"
            + createdAt
            + ",\"html_url\":"
            + (htmlUrl ?? Text(CommentUrl(id)))
            + ",\"body\":"
            + body
            + "}";
    }

    private static string Page(params string[] comments)
    {
        return "[" + string.Join(separator: ',', values: comments) + "]";
    }

    private static HttpResponseMessage CommentsPage(string json, string? next = null)
    {
        return Responses.Json(json: json, nextLink: next is null ? null : "<" + next + ">; rel=\"next\"");
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static Func<int, HttpResponseMessage> Serve(string issue, string comments)
    {
        return index =>
            index switch
            {
                0 => Responses.Json(issue),
                1 => CommentsPage(comments),
                _ => Unexpected(index),
            };
    }

    private Task<IssuePlanStatus> ReadAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int issue = ISSUE
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        IssuePlanStatusReader reader = new(restClient: rest);

        return reader
            .ReadAsync(repository: repository, issue: issue, cancellationToken: this.CancellationToken())
            .AsTask();
    }

    private async Task<PlanFollowUpComment> SingleFollowUpAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(issue: Issue(), comments: Page(Comment(id: 1, body: PLAN), Comment(id: 2, body: body)))
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        return Assert.Single(result.Comments);
    }

    private async Task<TException> FailsAsync<TException>(string issue, string comments)
        where TException : Exception
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: issue, comments: comments));

        return await Assert.ThrowsAsync<TException>(() => this.ReadAsync(upstream));
    }

    [Fact]
    public async Task NoCommentsReportsNoPlanAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: Issue(), comments: Page()));

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.False(condition: result.HasPlan, userMessage: "Should have no plan");
        Assert.False(condition: result.Blocked, userMessage: "Should not be blocked");
        Assert.Null(result.PlanAt);
        Assert.Null(result.PlanBy);
        Assert.Empty(result.Comments);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: FirstCommentsUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task CommentsWithoutPlanHeadingReportNoPlanAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(),
                comments: Page(
                    Comment(id: 1, body: "approved"),
                    Comment(id: 2, body: "## Implementation Plan v2"),
                    Comment(id: 3, body: " ## Implementation Plan"),
                    Comment(id: 4, body: "## implementation plan")
                )
            )
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.False(condition: result.HasPlan, userMessage: "Should have no plan");
        Assert.Null(result.PlanBy);
        Assert.Empty(result.Comments);
    }

    [Fact]
    public async Task OnePlanReportsAuthorAndLaterCommentsOldestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(),
                comments: Page(
                    Comment(id: 1, body: "Before the plan approved", login: "early"),
                    Comment(
                        id: 2,
                        body: PLAN,
                        login: "planner",
                        association: "MEMBER",
                        createdAt: "2026-01-03T00:00:00Z"
                    ),
                    Comment(id: 3, body: "First", login: "bob", association: "NONE", createdAt: "2026-01-04T00:00:00Z"),
                    Comment(
                        id: 4,
                        body: "LGTM",
                        login: "carol",
                        association: "OWNER",
                        createdAt: "2026-01-05T00:00:00Z"
                    )
                )
            )
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.HasPlan, userMessage: "Should have a plan");
        Assert.False(condition: result.Blocked, userMessage: "Should not be blocked");
        Assert.Equal(expected: "2026-01-03T00:00:00Z", actual: result.PlanAt);

        PlanAuthor planBy = Assert.IsType<PlanAuthor>(result.PlanBy);
        Assert.Equal(expected: "planner", actual: planBy.Login);
        Assert.Equal(expected: "MEMBER", actual: planBy.Association);
        Assert.Equal(expected: new Uri(CommentUrl(2)), actual: planBy.Url);

        Assert.Collection(
            collection: result.Comments,
            static first =>
            {
                Assert.Equal(expected: "2026-01-04T00:00:00Z", actual: first.CreatedAt);
                Assert.Equal(expected: "bob", actual: first.Login);
                Assert.Equal(expected: "NONE", actual: first.Association);
                Assert.False(condition: first.HasApprovalKeyword, userMessage: "First comment should not approve");
                Assert.Equal(expected: new Uri(CommentUrl(3)), actual: first.Url);
            },
            static second =>
            {
                Assert.Equal(expected: "2026-01-05T00:00:00Z", actual: second.CreatedAt);
                Assert.Equal(expected: "carol", actual: second.Login);
                Assert.Equal(expected: "OWNER", actual: second.Association);
                Assert.True(condition: second.HasApprovalKeyword, userMessage: "Second comment should approve");
                Assert.Equal(expected: new Uri(CommentUrl(4)), actual: second.Url);
            }
        );
    }

    [Fact]
    public async Task LatestPlanWinsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(),
                comments: Page(
                    Comment(id: 1, body: PLAN, login: "first"),
                    Comment(id: 2, body: "approved"),
                    Comment(id: 3, body: PLAN, login: "second", createdAt: "2026-02-01T00:00:00Z"),
                    Comment(id: 4, body: "Questions")
                )
            )
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.HasPlan, userMessage: "Should have a plan");
        Assert.Equal(expected: "2026-02-01T00:00:00Z", actual: result.PlanAt);
        Assert.Equal(expected: "second", actual: result.PlanBy?.Login);

        PlanFollowUpComment comment = Assert.Single(result.Comments);
        Assert.Equal(expected: new Uri(CommentUrl(4)), actual: comment.Url);
        Assert.False(condition: comment.HasApprovalKeyword, userMessage: "Comment should not approve");
    }

    [Fact]
    public async Task PlanAsLastCommentHasNoFollowUpsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(issue: Issue(), comments: Page(Comment(id: 1, body: "approved"), Comment(id: 2, body: PLAN)))
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.HasPlan, userMessage: "Should have a plan");
        Assert.Empty(result.Comments);
    }

    [Fact]
    public async Task CrLfPlanHeadingCountsAsPlanAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(issue: Issue(), comments: Page(Comment(id: 1, body: "Intro\r\n## Implementation Plan\r\nStep")))
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.HasPlan, userMessage: "Should have a plan");
    }

    [Theory]
    [InlineData("approved", true)]
    [InlineData("Approved, go ahead", true)]
    [InlineData("lgtm", true)]
    [InlineData("_approved_", true)]
    [InlineData("**approved**", true)]
    [InlineData("> approved", false)]
    [InlineData("> approved\nThanks", false)]
    [InlineData("unapproved", false)]
    [InlineData("approvedness", false)]
    [InlineData("approved_by", false)]
    [InlineData("Hello", false)]
    public async Task FollowUpReportsApprovalKeywordAsync(string body, bool expected)
    {
        PlanFollowUpComment comment = await this.SingleFollowUpAsync(body);

        Assert.Equal(expected: expected, actual: comment.HasApprovalKeyword);
    }

    [Theory]
    [InlineData("[{\"name\":\"Blocked\"}]")]
    [InlineData("[{\"name\":\"blocked\"}]")]
    [InlineData("[{\"name\":\"BLOCKED\"}]")]
    [InlineData("[{\"name\":\"bug\"},{\"name\":\"Blocked\"}]")]
    public async Task BlockedLabelInAnyCaseReportsBlockedAsync(string labels)
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: Issue(labels: labels), comments: Page()));

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.Blocked, userMessage: "Should be blocked");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[{\"name\":\"bug\"},{\"name\":\"Blocked-ish\"}]")]
    public async Task OtherLabelsReportNotBlockedAsync(string labels)
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: Issue(labels: labels), comments: Page()));

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.False(condition: result.Blocked, userMessage: "Should not be blocked");
    }

    [Fact]
    public async Task DeletedAuthorReportsEmptyLoginAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(),
                comments: Page(RawComment(id: 1, user: "null", body: Text(PLAN)), RawComment(id: 2, user: "null"))
            )
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.Equal(expected: string.Empty, actual: result.PlanBy?.Login);
        Assert.Equal(expected: string.Empty, actual: Assert.Single(result.Comments).Login);
    }

    [Fact]
    public async Task ControlCharactersInLoginAndAssociationAreReplacedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(),
                comments: Page(
                    Comment(id: 1, body: PLAN, login: "plan\nner", association: "MEM\u2028BER"),
                    Comment(id: 2, login: "bo\rb", association: "OW\tNER")
                )
            )
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.Equal(expected: "plan ner", actual: result.PlanBy?.Login);
        Assert.Equal(expected: "MEM BER", actual: result.PlanBy?.Association);

        PlanFollowUpComment comment = Assert.Single(result.Comments);
        Assert.Equal(expected: "bo b", actual: comment.Login);
        Assert.Equal(expected: "OW NER", actual: comment.Association);
    }

    [Fact]
    public async Task ReadsEveryCommentPageAsync()
    {
        const string secondPage = COMMENTS_URI + "?per_page=100&page=2";

        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(Issue()),
                1 => CommentsPage(json: Page(Comment(id: 1, body: PLAN)), next: secondPage),
                2 => CommentsPage(Page(Comment(id: 2, body: "approved"))),
                _ => Unexpected(index),
            }
        );

        IssuePlanStatus result = await this.ReadAsync(upstream);

        Assert.True(condition: result.HasPlan, userMessage: "Should have a plan");
        Assert.True(
            condition: Assert.Single(result.Comments).HasApprovalKeyword,
            userMessage: "Comment should approve"
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: new Uri(secondPage), actual: upstream.Requests[2].RequestUri);
    }

    [Fact]
    public async Task PullRequestLinkReportsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(Issue(pullRequest: "{\"url\":\"https://api.github.com/x\"}"))
                : Unexpected(index)
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task PullRequestHtmlUrlReportsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Json(Issue(htmlUrl: "https://github.com/credfeto/credfeto-workflow-mcp/pull/17"))
                : Unexpected(index)
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task MissingIssueReportsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task ServerErrorReportsViewFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        IssueViewFailedException exception = await Assert.ThrowsAsync<IssueViewFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task InvalidIssueJsonReportsViewFailedAsync()
    {
        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(
            issue: "not json",
            comments: Page()
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task InvalidCommentsJsonReportsViewFailedAsync()
    {
        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(
            issue: Issue(),
            comments: "not json"
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"name\":null}]")]
    [InlineData("[{\"name\":\"bug\"},null,{\"name\":\"Blocked\"}]")]
    public async Task LabelWithoutNameReportsViewFailedAsync(string labels)
    {
        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(
            issue: Issue(labels: labels),
            comments: Page()
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: "GitHub returned a label without a name", actual: inner.Message);
    }

    public static TheoryData<string, string> InvalidComments =>
        new()
        {
            { "null", "GitHub returned an empty comment" },
            { RawComment(id: 1, createdAt: "null"), "GitHub returned a comment without a valid creation time" },
            {
                RawComment(id: 1, createdAt: "\"yesterday\""),
                "GitHub returned a comment without a valid creation time"
            },
            { RawComment(id: 1, htmlUrl: "null"), "GitHub returned a comment without a valid link" },
            { RawComment(id: 1, htmlUrl: "\"issues/17\""), "GitHub returned a comment without a valid link" },
            { RawComment(id: 1, htmlUrl: "\"ftp://github.com/x\""), "GitHub returned a comment without a valid link" },
            { RawComment(id: 1, association: "null"), "GitHub returned a comment without an author association" },
            { RawComment(id: 1, body: "null"), "GitHub returned a comment without a body" },
            {
                RawComment(id: 1, user: "{\"login\":null,\"type\":\"User\"}"),
                "GitHub returned a comment author without a login"
            },
        };

    [Theory]
    [MemberData(nameof(InvalidComments))]
    public async Task InvalidCommentReportsViewFailedAsync(string comment, string expected)
    {
        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(
            issue: Issue(),
            comments: Page(comment)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: expected, actual: inner.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task MalformedRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task IssueNotAboveZeroIsRejectedWithoutRequestAsync(int issue)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, issue: issue)
        );

        Assert.Equal(expected: "issue", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
