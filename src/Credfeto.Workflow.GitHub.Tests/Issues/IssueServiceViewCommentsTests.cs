using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueServiceViewCommentsTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 16;
    private const string NOT_FOUND = "issue credfeto/credfeto-workflow-mcp#16 was not found";
    private const string INCOMPLETE = "could not read all the comments of credfeto/credfeto-workflow-mcp#16";
    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/16";
    private const string CREATED = "2026-01-02T03:04:05Z";
    private const string UPDATED = "2026-01-03T04:05:06Z";

    private const string COMMENTS_URI =
        GitHubServices.ApiBaseUrl + "repos/credfeto/credfeto-workflow-mcp/issues/16/comments";

    private static string Issue(int comments, string extra = "")
    {
        return "{\"title\":\"Fingerprint input\",\"state\":\"open\","
            + extra
            + "\"comments\":"
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
        string? updatedAt = UPDATED,
        string? body = "Hello"
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
            + "\""
            + (updatedAt is null ? string.Empty : ",\"updated_at\":\"" + updatedAt + "\"")
            + ",\"html_url\":\""
            + ISSUE_URL
            + "#issuecomment-"
            + idText
            + "\""
            + (body is null ? string.Empty : ",\"body\":\"" + JsonEncodedText.Encode(body) + "\"")
            + "}";
    }

    private static string Page(IEnumerable<string> comments)
    {
        return "[" + string.Join(separator: ',', values: comments) + "]";
    }

    private static string Page(params string[] comments)
    {
        return Page((IEnumerable<string>)comments);
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
                1 => Responses.Json(comments),
                _ => Unexpected(index),
            };
    }

    private IssueService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: GetSubstitute<ILeakChecker>());
    }

    private async Task<IssueViewResponse> ViewAsync(RecordingHttpMessageHandler upstream)
    {
        return await this.CreateService(upstream)
            .ViewAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                includeComments: true,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<IReadOnlyList<IssueViewComment>> ServedAsync(string issue, string comments)
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: issue, comments: comments));

        IssueViewResponse response = await this.ViewAsync(upstream);

        return Assert.IsAssignableFrom<IReadOnlyList<IssueViewComment>>(response.Comments);
    }

    private async Task AssertIncompleteAsync(string issue, string comments)
    {
        using RecordingHttpMessageHandler upstream = new(Serve(issue: issue, comments: comments));

        IssueCommentsIncompleteException exception = await Assert.ThrowsAsync<IssueCommentsIncompleteException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    [Fact]
    public async Task CommentsAreReturnedOldestFirstWithTheirEditTimesAsync()
    {
        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(
            issue: Issue(2),
            comments: Page(Comment(1), Comment(id: 2, login: "bob", updatedAt: "2026-02-03T04:05:06Z", body: "Bye"))
        );

        Assert.Equal(
            expected:
            [
                new IssueViewComment(Id: "1", Author: "alice", CreatedAt: CREATED, UpdatedAt: UPDATED, Body: "Hello"),
                new IssueViewComment(
                    Id: "2",
                    Author: "bob",
                    CreatedAt: CREATED,
                    UpdatedAt: "2026-02-03T04:05:06Z",
                    Body: "Bye"
                ),
            ],
            actual: comments
        );
    }

    [Fact]
    public async Task MilestoneIsReturnedWithTheCommentsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Serve(
                issue: Issue(comments: 1, extra: "\"milestone\":{\"title\":\"Phase 2\"},"),
                comments: Page(Comment(1))
            )
        );

        IssueViewResponse response = await this.ViewAsync(upstream);

        Assert.Equal(expected: "Phase 2", actual: response.Milestone);
        _ = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<IssueViewComment>>(response.Comments));
    }

    [Fact]
    public async Task BodyIsReturnedWholeAndUnmodifiedAsync()
    {
        string body =
            "first\r\nsecond\u001b[31m\u2028third\n"
            + string.Join(separator: '\n', values: Enumerable.Range(start: 1, count: 600))
            + "\n\n";

        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(
            issue: Issue(1),
            comments: Page(Comment(id: 1, body: body))
        );

        Assert.Equal(expected: body, actual: Assert.Single(comments).Body);
    }

    [Fact]
    public async Task EmptyBodyIsReturnedAsEmptyAsync()
    {
        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(
            issue: Issue(1),
            comments: Page(Comment(id: 1, body: string.Empty))
        );

        Assert.Equal(expected: string.Empty, actual: Assert.Single(comments).Body);
    }

    [Fact]
    public async Task BotIsReportedAsAppAsync()
    {
        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(
            issue: Issue(1),
            comments: Page(Comment(id: 1, login: "github-actions[bot]", type: "Bot"))
        );

        Assert.Equal(expected: "app/github-actions", actual: Assert.Single(comments).Author);
    }

    [Fact]
    public async Task DeletedAccountHasEmptyAuthorAsync()
    {
        string comment = Mutate(
            source: Comment(1),
            from: "\"user\":{\"login\":\"alice\",\"type\":\"User\"}",
            to: "\"user\":null"
        );

        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(issue: Issue(1), comments: Page(comment));

        Assert.Equal(expected: string.Empty, actual: Assert.Single(comments).Author);
    }

    [Fact]
    public async Task EveryCommentIsReturnedAcrossPagesAsync()
    {
        const string next = COMMENTS_URI + "?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(Issue(150)),
                1 => Responses.Json(
                    json: Page(Enumerable.Range(start: 1, count: 100).Select(id => Comment(id))),
                    nextLink: "<" + next + ">; rel=\"next\""
                ),
                2 => Responses.Json(Page(Enumerable.Range(start: 101, count: 50).Select(id => Comment(id)))),
                _ => Unexpected(index),
            }
        );

        IssueViewResponse response = await this.ViewAsync(upstream);

        IReadOnlyList<IssueViewComment> comments = Assert.IsAssignableFrom<IReadOnlyList<IssueViewComment>>(
            response.Comments
        );
        Assert.Equal(expected: 150, actual: comments.Count);
        Assert.Equal(expected: "1", actual: comments[0].Id);
        Assert.Equal(expected: "150", actual: comments[^1].Id);
        Assert.Equal(expected: new(COMMENTS_URI + "?per_page=100"), actual: upstream.Requests[1].RequestUri);
        Assert.Equal(expected: new(next), actual: upstream.Requests[2].RequestUri);
    }

    [Fact]
    public async Task IssueWithNoCommentsIsEmptyAsync()
    {
        IReadOnlyList<IssueViewComment> comments = await this.ServedAsync(issue: Issue(0), comments: Page());

        Assert.Empty(comments);
    }

    [Fact]
    public Task FewerCommentsThanTheIssueCountIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(2), comments: Page(Comment(1)));
    }

    [Fact]
    public Task MoreCommentsThanTheIssueCountIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: Page(Comment(1), Comment(2)));
    }

    [Fact]
    public Task MissingEditTimeIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: Page(Comment(id: 1, updatedAt: null)));
    }

    [Theory]
    [InlineData("2026-01-03")]
    [InlineData("2026-01-03T04:05:06+01:00")]
    [InlineData("later")]
    public Task MalformedEditTimeIsIncompleteAsync(string updatedAt)
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: Page(Comment(id: 1, updatedAt: updatedAt)));
    }

    [Fact]
    public Task MalformedCreationTimeIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: Page(Comment(id: 1, createdAt: "earlier")));
    }

    [Fact]
    public Task MissingBodyIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: Page(Comment(id: 1, body: null)));
    }

    [Fact]
    public Task MissingIdIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(
            issue: Issue(1),
            comments: Page(Mutate(source: Comment(1), from: "\"id\":1,", to: string.Empty))
        );
    }

    [Fact]
    public Task NullCommentIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(issue: Issue(1), comments: "[null]");
    }

    [Fact]
    public async Task PullRequestIsNotFoundWithoutReadingCommentsAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(Issue(comments: 1, extra: "\"pull_request\":{},")) : Unexpected(index)
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    private static string Mutate(string source, string from, string to)
    {
        Assert.Contains(expectedSubstring: from, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: from, newValue: to, comparisonType: StringComparison.Ordinal);
    }
}
