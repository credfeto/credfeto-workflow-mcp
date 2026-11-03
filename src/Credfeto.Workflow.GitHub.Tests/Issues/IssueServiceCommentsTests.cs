using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueServiceCommentsTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 16;
    private const string REFERENCE = "credfeto/credfeto-workflow-mcp#16";
    private const string NOT_FOUND = "issue credfeto/credfeto-workflow-mcp#16 was not found";
    private const string FAILED = "could not read issue credfeto/credfeto-workflow-mcp#16";
    private const string INCOMPLETE = "could not read all the comments of credfeto/credfeto-workflow-mcp#16";
    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/16";
    private const string CREATED = "2026-01-02T03:04:05Z";

    private const string COMMENTS_URI =
        GitHubServices.ApiBaseUrl + "repos/credfeto/credfeto-workflow-mcp/issues/16/comments";

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/16");

    private static readonly Uri FirstCommentsUri = new(COMMENTS_URI + "?per_page=100");

    private static string Issue(int comments)
    {
        return "{\"comments\":"
            + comments.ToString(CultureInfo.InvariantCulture)
            + ",\"html_url\":\""
            + ISSUE_URL
            + "\"}";
    }

    private static string Comment(
        long id,
        string login = "alice",
        string type = "User",
        string createdAt = CREATED,
        string body = "Hello"
    )
    {
        string idText = id.ToString(CultureInfo.InvariantCulture);

        return "{\"id\":"
            + idText
            + ",\"user\":{\"login\":\""
            + login
            + "\",\"type\":\""
            + type
            + "\"},\"author_association\":\"OWNER\",\"created_at\":\""
            + createdAt
            + "\",\"html_url\":\""
            + ISSUE_URL
            + "#issuecomment-"
            + idText
            + "\",\"body\":\""
            + JsonEncodedText.Encode(body).ToString()
            + "\"}";
    }

    private static string Page(IEnumerable<string> comments)
    {
        return "[" + string.Join(separator: ',', values: comments) + "]";
    }

    private static string Page(params string[] comments)
    {
        return Page((IEnumerable<string>)comments);
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

    private IssueService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<IssueComments> CommentsAsync(
        RecordingHttpMessageHandler upstream,
        string? since = null,
        string? author = null
    )
    {
        return await this.CreateService(upstream)
            .CommentsAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                since: since,
                author: author,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<IssueComments> ServedAsync(
        string issue,
        string comments,
        string? since = null,
        string? author = null
    )
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: issue, comments: comments));

        return await this.CommentsAsync(upstream: upstream, since: since, author: author);
    }

    private Task<TException> FailsAsync<TException>(RecordingHttpMessageHandler upstream)
        where TException : Exception
    {
        return Assert.ThrowsAsync<TException>(() => this.CommentsAsync(upstream));
    }

    private async Task AssertIncompleteAsync(string issue, string comments)
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: issue, comments: comments));

        IssueCommentsIncompleteException exception = await this.FailsAsync<IssueCommentsIncompleteException>(upstream);

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    private async Task AssertIssueFailedAsync(string issue)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(issue) : Unexpected(index)
        );

        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    private async Task AssertRejectedAsync(
        string repository,
        int issue,
        string? since,
        string? author,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .CommentsAsync(
                    repository: repository,
                    issue: issue,
                    since: since,
                    author: author,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        return this.AssertRejectedAsync(
            repository: repository,
            issue: ISSUE,
            since: null,
            author: null,
            parameterName: "repository"
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task NonPositiveIssueIsRejectedBeforeAnyRequestAsync(int issue)
    {
        return this.AssertRejectedAsync(
            repository: REPOSITORY,
            issue: issue,
            since: null,
            author: null,
            parameterName: "issue"
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-01-02")]
    [InlineData("2026-01-02T03:04:05")]
    [InlineData("2026-01-02T03:04:05+01:00")]
    [InlineData("2026-13-02T03:04:05Z")]
    [InlineData("yesterday")]
    public Task MalformedSinceIsRejectedBeforeAnyRequestAsync(string since)
    {
        return this.AssertRejectedAsync(
            repository: REPOSITORY,
            issue: ISSUE,
            since: since,
            author: null,
            parameterName: "since"
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("-alice")]
    [InlineData("alice bob")]
    [InlineData("app/")]
    [InlineData("app/dependabot[bot]")]
    [InlineData("dependabot[bot]")]
    public Task MalformedAuthorIsRejectedBeforeAnyRequestAsync(string author)
    {
        return this.AssertRejectedAsync(
            repository: REPOSITORY,
            issue: ISSUE,
            since: null,
            author: author,
            parameterName: "author"
        );
    }

    [Fact]
    public async Task CommentsAreReturnedOldestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(2),
                comments: Page(
                    Comment(id: 11, body: "First"),
                    Comment(id: 12, login: "bob", createdAt: "2026-01-03T00:00:00Z", body: "Second")
                )
            )
        );

        IssueComments result = await this.CommentsAsync(upstream);

        Assert.Equal(expected: REFERENCE, actual: result.Issue);
        Assert.Equal(expected: new(ISSUE_URL), actual: result.Url);
        Assert.Equal(
            expected: new IssueCommentsSummary(Printed: 2, Matched: 2, Total: 2, Omitted: 0),
            actual: result.Summary
        );
        Assert.Equal(expected: 2, actual: result.Comments.Count);

        IssueComment first = result.Comments[0];
        Assert.Equal(expected: "11", actual: first.Id);
        Assert.Equal(expected: "alice", actual: first.Author);
        Assert.Equal(expected: "OWNER", actual: first.Association);
        Assert.Equal(expected: CREATED, actual: first.CreatedAt);
        Assert.Equal(expected: new(ISSUE_URL + "#issuecomment-11"), actual: first.Url);
        Assert.False(first.Truncated, userMessage: "a short body is not truncated");
        Assert.Equal(expected: ["First"], actual: first.Body);

        Assert.Equal(expected: "12", actual: result.Comments[1].Id);
        Assert.Equal(expected: "bob", actual: result.Comments[1].Author);

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: FirstCommentsUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task IssueWithNoCommentsIsEmptyAsync()
    {
        IssueComments result = await this.ServedAsync(issue: Issue(0), comments: "[]");

        Assert.Empty(result.Comments);
        Assert.Equal(
            expected: new IssueCommentsSummary(Printed: 0, Matched: 0, Total: 0, Omitted: 0),
            actual: result.Summary
        );
    }

    [Fact]
    public async Task PullRequestNumberIsAcceptedAsync()
    {
        const string pullRequest =
            "{\"comments\":1,\"html_url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/16\","
            + "\"pull_request\":{\"url\":\"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/16\"}}";

        IssueComments result = await this.ServedAsync(issue: pullRequest, comments: Page(Comment(1)));

        Assert.Equal(expected: new("https://github.com/credfeto/credfeto-workflow-mcp/pull/16"), actual: result.Url);
        _ = Assert.Single(result.Comments);
    }

    [Fact]
    public async Task SinceKeepsCommentsCreatedAtOrAfterItAsync()
    {
        IssueComments result = await this.ServedAsync(
            issue: Issue(3),
            comments: Page(
                Comment(id: 1, createdAt: "2026-01-02T03:04:04Z"),
                Comment(id: 2, createdAt: "2026-01-02T03:04:05Z"),
                Comment(id: 3, createdAt: "2026-01-02T03:04:06Z")
            ),
            since: "2026-01-02T03:04:05Z"
        );

        Assert.Equal(
            expected: ["2", "3"],
            actual: result.Comments.Select(comment => comment.Id),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(
            expected: new IssueCommentsSummary(Printed: 2, Matched: 2, Total: 3, Omitted: 0),
            actual: result.Summary
        );
    }

    [Fact]
    public async Task AuthorIsMatchedCaseInsensitivelyAsync()
    {
        IssueComments result = await this.ServedAsync(
            issue: Issue(3),
            comments: Page(Comment(id: 1, login: "Alice"), Comment(id: 2, login: "bob"), Comment(id: 3)),
            author: "ALICE"
        );

        Assert.Equal(
            expected: ["1", "3"],
            actual: result.Comments.Select(comment => comment.Id),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(expected: 2, actual: result.Summary.Matched);
        Assert.Equal(expected: 3, actual: result.Summary.Total);
    }

    [Fact]
    public async Task BotIsReportedAndMatchedAsAppAsync()
    {
        IssueComments result = await this.ServedAsync(
            issue: Issue(2),
            comments: Page(Comment(id: 1, login: "dependabot[bot]", type: "Bot"), Comment(id: 2)),
            author: "app/Dependabot"
        );

        IssueComment comment = Assert.Single(result.Comments);
        Assert.Equal(expected: "1", actual: comment.Id);
        Assert.Equal(expected: "app/dependabot", actual: comment.Author);
    }

    [Fact]
    public async Task OtherAccountTypeIsPrefixedAsync()
    {
        IssueComments result = await this.ServedAsync(
            issue: Issue(1),
            comments: Page(Comment(id: 1, login: "acme", type: "Organization"))
        );

        Assert.Equal(expected: "organization/acme", actual: Assert.Single(result.Comments).Author);
    }

    [Fact]
    public async Task DeletedAccountHasEmptyAuthorAsync()
    {
        string comment = Comment(1)
            .Replace(
                oldValue: "{\"login\":\"alice\",\"type\":\"User\"}",
                newValue: "null",
                comparisonType: StringComparison.Ordinal
            );

        IssueComments result = await this.ServedAsync(issue: Issue(1), comments: Page(comment));

        Assert.Equal(expected: string.Empty, actual: Assert.Single(result.Comments).Author);
    }

    [Fact]
    public async Task BodyIsSplitIntoLinesAsync()
    {
        IssueComments result = await this.ServedAsync(
            issue: Issue(1),
            comments: Page(Comment(id: 1, body: "One\r\nTwo\n\nFour\r\n\r\n"))
        );

        Assert.Equal(expected: ["One", "Two", "", "Four"], actual: Assert.Single(result.Comments).Body);
    }

    [Fact]
    public async Task EmptyBodyHasNoLinesAsync()
    {
        IssueComments result = await this.ServedAsync(issue: Issue(1), comments: Page(Comment(id: 1, body: "")));

        IssueComment comment = Assert.Single(result.Comments);
        Assert.Empty(comment.Body);
        Assert.False(comment.Truncated, userMessage: "an empty body is not truncated");
    }

    [Fact]
    public async Task LongBodyIsTruncatedAsync()
    {
        string body = string.Join(
            separator: '\n',
            values: Enumerable.Range(start: 1, count: 501).Select(line => line.ToString(CultureInfo.InvariantCulture))
        );

        IssueComments result = await this.ServedAsync(issue: Issue(1), comments: Page(Comment(id: 1, body: body)));

        IssueComment comment = Assert.Single(result.Comments);
        Assert.True(comment.Truncated, userMessage: "a body over the limit is truncated");
        Assert.Equal(expected: 500, actual: comment.Body.Count);
        Assert.Equal(expected: "500", actual: comment.Body[^1]);
    }

    [Fact]
    public async Task BodyOfExactlyTheLimitIsNotTruncatedAsync()
    {
        string body = string.Join(separator: '\n', values: Enumerable.Repeat(element: "x", count: 500));

        IssueComments result = await this.ServedAsync(issue: Issue(1), comments: Page(Comment(id: 1, body: body)));

        IssueComment comment = Assert.Single(result.Comments);
        Assert.False(comment.Truncated, userMessage: "a body at the limit is not truncated");
        Assert.Equal(expected: 500, actual: comment.Body.Count);
    }

    [Fact]
    public async Task OnlyTheMostRecentHundredAreReturnedAcrossPagesAsync()
    {
        const string next = COMMENTS_URI + "?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(Issue(120)),
                1 => CommentsPage(
                    json: Page(Enumerable.Range(start: 1, count: 100).Select(id => Comment(id))),
                    next: next
                ),
                2 => CommentsPage(Page(Enumerable.Range(start: 101, count: 20).Select(id => Comment(id)))),
                _ => Unexpected(index),
            }
        );

        IssueComments result = await this.CommentsAsync(upstream);

        Assert.Equal(
            expected: new IssueCommentsSummary(Printed: 100, Matched: 120, Total: 120, Omitted: 20),
            actual: result.Summary
        );
        Assert.Equal(expected: "21", actual: result.Comments[0].Id);
        Assert.Equal(expected: "120", actual: result.Comments[^1].Id);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: new(next), actual: upstream.Requests[2].RequestUri);
    }

    [Fact]
    public Task FewerCommentsThanTheIssueCountIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(2), comments: Page(Comment(1)));
    }

    [Fact]
    public Task MoreCommentsThanTheIssueCountIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(0), comments: Page(Comment(1)));
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(-1)]
    public async Task CommentCountOutOfRangeIsIncompleteBeforeReadingCommentsAsync(int count)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(Issue(count)) : Unexpected(index)
        );

        IssueCommentsIncompleteException exception = await this.FailsAsync<IssueCommentsIncompleteException>(upstream);

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public Task MissingCommentCountIsFailedAsync()
    {
        return this.AssertIssueFailedAsync("{\"html_url\":\"" + ISSUE_URL + "\"}");
    }

    [Theory]
    [InlineData("{\"comments\":0}")]
    [InlineData("{\"comments\":0,\"html_url\":\"/credfeto/credfeto-workflow-mcp/issues/16\"}")]
    [InlineData("{\"comments\":0,\"html_url\":\"ftp://github.com/credfeto/credfeto-workflow-mcp/issues/16\"}")]
    public Task InvalidIssueLinkIsFailedAsync(string issue)
    {
        return this.AssertIssueFailedAsync(issue);
    }

    [Theory]
    [InlineData("\"id\":1,", "\"id\":0,")]
    [InlineData("\"id\":1,", "\"id\":null,")]
    [InlineData("\"type\":\"User\"", "\"type\":null")]
    [InlineData("\"login\":\"alice\"", "\"login\":null")]
    [InlineData("\"author_association\":\"OWNER\"", "\"author_association\":null")]
    [InlineData("\"created_at\":\"2026-01-02T03:04:05Z\"", "\"created_at\":null")]
    [InlineData("\"created_at\":\"2026-01-02T03:04:05Z\"", "\"created_at\":\"2026-01-02\"")]
    [InlineData("\"html_url\":\"https://", "\"html_url\":\"ftp://")]
    [InlineData("\"html_url\":\"https://github.com", "\"html_url\":\"/relative")]
    [InlineData("\"body\":\"Hello\"", "\"body\":null")]
    public Task MalformedCommentIsIncompleteAsync(string from, string to)
    {
        string comment = Comment(1);
        Assert.Contains(expectedSubstring: from, actualString: comment, comparisonType: StringComparison.Ordinal);

        return this.AssertIncompleteAsync(
            issue: Issue(1),
            comments: Page(comment.Replace(oldValue: from, newValue: to, comparisonType: StringComparison.Ordinal))
        );
    }

    [Fact]
    public Task NullCommentIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: "[null]");
    }

    [Fact]
    public async Task MissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await this.FailsAsync<IssueNotFoundException>(upstream);

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task CommentsPageFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(Issue(1)) : Responses.Status(HttpStatusCode.BadGateway)
        );

        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Fact]
    public async Task UnreadableCommentsPageIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(Issue(1)) : Responses.Html("<html></html>")
        );

        IssueViewFailedException exception = await this.FailsAsync<IssueViewFailedException>(upstream);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: Issue(1), comments: Page(Comment(1))));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateService(upstream)
                .CommentsAsync(
                    repository: REPOSITORY,
                    issue: ISSUE,
                    since: null,
                    author: null,
                    cancellationToken: cancelled.Token
                )
                .AsTask()
        );
    }
}
