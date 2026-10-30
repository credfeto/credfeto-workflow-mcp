using System;
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
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PullRequestCommentsReaderTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;
    private const string REFERENCE = REPOSITORY + "#41";
    private const string INCOMPLETE = "could not read all the comments of " + REFERENCE;

    private const string COMMENT_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/41#issuecomment-1";
    private const string REVIEW_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/41#pullrequestreview-2";
    private const string INLINE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/41#discussion_r3";

    private const string USER_AUTHOR = """{"__typename":"User","login":"alice"}""";

    private const string COMMENT =
        "{\"fullDatabaseId\":\"1\",\"author\":"
        + USER_AUTHOR
        + ",\"authorAssociation\":\"OWNER\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"isMinimized\":false,\"url\":\""
        + COMMENT_URL
        + "\",\"body\":\"hello\"}";

    private const string REVIEW =
        "{\"fullDatabaseId\":\"2\",\"author\":{\"__typename\":\"Bot\",\"login\":\"copilot\"},\"authorAssociation\":\"NONE\",\"state\":\"APPROVED\",\"submittedAt\":\"2026-01-02T00:00:00Z\",\"url\":\""
        + REVIEW_URL
        + "\",\"body\":\"looks good\"}";

    private const string INLINE =
        "{\"fullDatabaseId\":\"3\",\"author\":{\"__typename\":\"Mannequin\",\"login\":\"ghost\"},\"authorAssociation\":\"MEMBER\",\"createdAt\":\"2026-01-03T00:00:00Z\",\"isMinimized\":true,\"url\":\""
        + INLINE_URL
        + "\",\"body\":\"fix this\",\"replyTo\":{\"fullDatabaseId\":\"4\"},\"pullRequestReview\":{\"fullDatabaseId\":\"2\"}}";

    private const string THREAD_COMMENTS =
        "\"comments\":{\"totalCount\":1,\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":["
        + INLINE
        + "]}";

    private const string THREAD =
        "{\"isResolved\":false,\"isOutdated\":true,\"path\":\"src/Program.cs\",\"line\":10,\"originalLine\":12,"
        + THREAD_COMMENTS
        + "}";

    private const string EMPTY_CONNECTION =
        """{"totalCount":0,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[]}""";

    private const string COMMENTS_CONNECTION =
        """{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[""" + COMMENT + "]}";

    private static readonly PullRequestComment ExpectedComment = new(
        Id: "1",
        Author: "alice",
        Association: "OWNER",
        CreatedAt: "2026-01-01T00:00:00Z",
        Minimized: false,
        Url: new(COMMENT_URL),
        Body: "hello"
    );

    private static readonly PullRequestReview ExpectedReview = new(
        Id: "2",
        Author: "app/copilot",
        Association: "NONE",
        State: "APPROVED",
        SubmittedAt: "2026-01-02T00:00:00Z",
        Url: new(REVIEW_URL),
        Body: "looks good"
    );

    private static readonly PullRequestInlineComment ExpectedInline = new(
        Id: "3",
        InReplyTo: "4",
        ReviewId: "2",
        Author: "mannequin/ghost",
        Association: "MEMBER",
        CreatedAt: "2026-01-03T00:00:00Z",
        Minimized: true,
        Url: new(INLINE_URL),
        Body: "fix this"
    );

    [Fact]
    public async Task WholePullRequestIsReadInOneRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                comments: Connection(totalCount: 1, nodes: COMMENT),
                reviews: Connection(totalCount: 1, nodes: REVIEW),
                threads: Connection(totalCount: 1, nodes: THREAD)
            )
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(expected: REFERENCE, actual: result.Pr);
        Assert.Equal(expected: "APPROVED", actual: result.ReviewDecision);
        Assert.Equal(expected: [ExpectedComment], actual: result.Comments);
        Assert.Equal(expected: [ExpectedReview], actual: result.Reviews);

        PullRequestThread thread = Assert.Single(result.Threads);
        Assert.Equal(expected: "3", actual: thread.Id);
        Assert.False(thread.Resolved, userMessage: "thread should not be resolved");
        Assert.True(thread.Outdated, userMessage: "thread should be outdated");
        Assert.Equal(expected: "src/Program.cs", actual: thread.Path);
        Assert.Equal(expected: "10", actual: thread.Line);
        Assert.Equal(expected: "12", actual: thread.OriginalLine);
        Assert.Equal(expected: 1, actual: thread.CommentCount);
        Assert.Equal(expected: [ExpectedInline], actual: thread.Comments);

        Assert.Equal(
            expected: new(Comments: 1, Reviews: 1, ReviewsEmpty: 0, Threads: 1, Unresolved: 1, Inline: 1),
            actual: result.Summary
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: PULL_REQUEST, actual: variables.GetProperty("n").GetInt32());
        Assert.True(variables.GetProperty("comments").GetBoolean(), userMessage: "comments should be requested");
        Assert.True(variables.GetProperty("reviews").GetBoolean(), userMessage: "reviews should be requested");
        Assert.True(variables.GetProperty("threads").GetBoolean(), userMessage: "threads should be requested");
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("commentsAfter").ValueKind);
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("reviewsAfter").ValueKind);
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("threadsAfter").ValueKind);
        Assert.Contains(
            expectedSubstring: "pullRequest(number:$n)",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task OnlyListsWithMorePagesAreRequestedAgainAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Page(
                    comments: Connection(
                        totalCount: 101,
                        nodes: Repeat(node: COMMENT, count: 100),
                        hasNextPage: true,
                        endCursor: "comments-1"
                    ),
                    reviews: Connection(totalCount: 1, nodes: REVIEW),
                    threads: Connection(
                        totalCount: 101,
                        nodes: Repeat(node: THREAD, count: 100),
                        hasNextPage: true,
                        endCursor: "threads-1"
                    )
                )
                : Page(
                    comments: Connection(totalCount: 101, nodes: COMMENT),
                    reviews: "null",
                    threads: Connection(totalCount: 101, nodes: THREAD)
                )
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: new(Comments: 101, Reviews: 1, ReviewsEmpty: 0, Threads: 101, Unresolved: 101, Inline: 101),
            actual: result.Summary
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument body = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.True(variables.GetProperty("comments").GetBoolean(), userMessage: "comments should be requested again");
        Assert.False(
            variables.GetProperty("reviews").GetBoolean(),
            userMessage: "reviews should not be requested again"
        );
        Assert.True(variables.GetProperty("threads").GetBoolean(), userMessage: "threads should be requested again");
        Assert.Equal(expected: "comments-1", actual: variables.GetProperty("commentsAfter").GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("reviewsAfter").ValueKind);
        Assert.Equal(expected: "threads-1", actual: variables.GetProperty("threadsAfter").GetString());
    }

    [Fact]
    public async Task ReviewsWithoutTextAreCountedButNotListedAsync()
    {
        string wrapper = Mutate(
            source: Mutate(source: REVIEW, oldValue: "\"state\":\"APPROVED\"", newValue: "\"state\":\"COMMENTED\""),
            oldValue: "\"body\":\"looks good\"",
            newValue: "\"body\":\" \\t\\r\\n\""
        );
        string commented = Mutate(
            source: REVIEW,
            oldValue: "\"state\":\"APPROVED\"",
            newValue: "\"state\":\"COMMENTED\""
        );
        string approvedWithoutText = Mutate(
            source: REVIEW,
            oldValue: "\"body\":\"looks good\"",
            newValue: "\"body\":\"\""
        );

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(reviews: Connection(totalCount: 3, nodes: wrapper + "," + commented + "," + approvedWithoutText))
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: [ExpectedReview with { State = "COMMENTED" }, ExpectedReview with { Body = "" }],
            actual: result.Reviews
        );
        Assert.Equal(
            expected: new(Comments: 0, Reviews: 2, ReviewsEmpty: 1, Threads: 0, Unresolved: 0, Inline: 0),
            actual: result.Summary
        );
    }

    [Theory]
    [InlineData(USER_AUTHOR, "alice")]
    [InlineData("""{"__typename":"Bot","login":"copilot"}""", "app/copilot")]
    [InlineData("""{"__typename":"Mannequin","login":"ghost"}""", "mannequin/ghost")]
    [InlineData("""{"__typename":"EnterpriseUserAccount","login":"corp"}""", "enterpriseuseraccount/corp")]
    [InlineData("null", "")]
    public async Task AuthorIsNamedByItsKindAsync(string author, string expected)
    {
        string comment = Mutate(source: COMMENT, oldValue: USER_AUTHOR, newValue: author);

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(comments: Connection(totalCount: 1, nodes: comment))
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(expected: [ExpectedComment with { Author = expected }], actual: result.Comments);
    }

    [Fact]
    public async Task HeaderValuesCannotStartNewLinesAsync()
    {
        string comment = Mutate(
            source: COMMENT,
            oldValue: "\"authorAssociation\":\"OWNER\"",
            newValue: "\"authorAssociation\":\"OW\\u0007NER\\u2028X\\u2029Y\\nZ\""
        );

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(comments: Connection(totalCount: 1, nodes: comment))
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(expected: [ExpectedComment with { Association = "OW NER X Y Z" }], actual: result.Comments);
    }

    [Fact]
    public async Task BodyKeepsAllButTrailingLineBreaksAsync()
    {
        string comment = Mutate(
            source: COMMENT,
            oldValue: "\"body\":\"hello\"",
            newValue: "\"body\":\"\\n hello \\r\\n\\n\""
        );

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(comments: Connection(totalCount: 1, nodes: comment))
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(expected: [ExpectedComment with { Body = "\n hello " }], actual: result.Comments);
    }

    [Fact]
    public async Task OptionalValuesMayBeMissingAsync()
    {
        string review = Mutate(
            source: REVIEW,
            oldValue: "\"submittedAt\":\"2026-01-02T00:00:00Z\"",
            newValue: "\"submittedAt\":null"
        );
        string thread = Mutate(
            source: Mutate(
                source: Mutate(source: THREAD, oldValue: "\"line\":10", newValue: "\"line\":null"),
                oldValue: "\"originalLine\":12",
                newValue: "\"originalLine\":null"
            ),
            oldValue: "\"replyTo\":{\"fullDatabaseId\":\"4\"},\"pullRequestReview\":{\"fullDatabaseId\":\"2\"}",
            newValue: "\"replyTo\":null,\"pullRequestReview\":null"
        );
        string comment = Mutate(source: COMMENT, oldValue: "\"url\":\"https:", newValue: "\"url\":\"http:");

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                comments: Connection(totalCount: 1, nodes: comment),
                reviews: Connection(totalCount: 1, nodes: review),
                threads: Connection(totalCount: 1, nodes: thread),
                reviewDecision: "null"
            )
        );

        PullRequestComments result = await this.ReadAsync(upstream);

        Assert.Equal(expected: string.Empty, actual: result.ReviewDecision);
        Assert.Equal(
            expected:
            [
                ExpectedComment with
                {
                    Url = new("http://github.com/credfeto/credfeto-workflow-mcp/pull/41#issuecomment-1"),
                },
            ],
            actual: result.Comments
        );
        Assert.Equal(expected: [ExpectedReview with { SubmittedAt = "" }], actual: result.Reviews);

        PullRequestThread projected = Assert.Single(result.Threads);
        Assert.Equal(expected: string.Empty, actual: projected.Line);
        Assert.Equal(expected: string.Empty, actual: projected.OriginalLine);
        Assert.Equal(expected: [ExpectedInline with { InReplyTo = "", ReviewId = "" }], actual: projected.Comments);
    }

    [Theory]
    [InlineData("\"fullDatabaseId\":\"1\"", "\"fullDatabaseId\":\"abc\"")]
    [InlineData("\"fullDatabaseId\":\"1\"", "\"fullDatabaseId\":\"1\\n\"")]
    [InlineData("\"fullDatabaseId\":\"1\"", "\"fullDatabaseId\":null")]
    [InlineData("\"__typename\":\"User\"", "\"__typename\":null")]
    [InlineData("\"login\":\"alice\"", "\"login\":null")]
    [InlineData("\"authorAssociation\":\"OWNER\"", "\"authorAssociation\":null")]
    [InlineData("\"createdAt\":\"2026-01-01T00:00:00Z\"", "\"createdAt\":null")]
    [InlineData("\"isMinimized\":false", "\"isMinimized\":null")]
    [InlineData("\"url\":\"" + COMMENT_URL + "\"", "\"url\":null")]
    [InlineData("\"url\":\"" + COMMENT_URL + "\"", "\"url\":\"pull/41#issuecomment-1\"")]
    [InlineData("\"url\":\"" + COMMENT_URL + "\"", "\"url\":\"ftp://github.com/pull/41\"")]
    [InlineData("\"body\":\"hello\"", "\"body\":null")]
    public async Task MalformedCommentIsIncompleteAsync(string oldValue, string newValue)
    {
        string comment = Mutate(source: COMMENT, oldValue: oldValue, newValue: newValue);

        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(comments: Connection(totalCount: 1, nodes: comment))
        );

        await this.ReadIncompleteAsync(upstream);
    }

    [Theory]
    [InlineData("\"state\":\"APPROVED\"", "\"state\":null")]
    [InlineData("\"fullDatabaseId\":\"2\"", "\"fullDatabaseId\":null")]
    [InlineData("\"url\":\"" + REVIEW_URL + "\"", "\"url\":null")]
    public async Task MalformedReviewIsIncompleteAsync(string oldValue, string newValue)
    {
        string review = Mutate(source: REVIEW, oldValue: oldValue, newValue: newValue);

        using RecordingHttpMessageHandler upstream = new(_ => Page(reviews: Connection(totalCount: 1, nodes: review)));

        await this.ReadIncompleteAsync(upstream);
    }

    [Theory]
    [InlineData("\"isResolved\":false", "\"isResolved\":null")]
    [InlineData("\"isOutdated\":true", "\"isOutdated\":null")]
    [InlineData("\"path\":\"src/Program.cs\"", "\"path\":null")]
    [InlineData(THREAD_COMMENTS, "\"comments\":null")]
    [InlineData("\"totalCount\":1", "\"totalCount\":null")]
    [InlineData("\"totalCount\":1", "\"totalCount\":0")]
    [InlineData("\"totalCount\":1", "\"totalCount\":101")]
    [InlineData("\"totalCount\":1", "\"totalCount\":2")]
    [InlineData("\"hasNextPage\":false", "\"hasNextPage\":true")]
    [InlineData("\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}", "\"pageInfo\":null")]
    [InlineData("\"nodes\":[" + INLINE + "]", "\"nodes\":null")]
    [InlineData("\"nodes\":[" + INLINE + "]", "\"nodes\":[null]")]
    [InlineData("\"fullDatabaseId\":\"3\"", "\"fullDatabaseId\":null")]
    [InlineData("\"isMinimized\":true", "\"isMinimized\":null")]
    [InlineData("\"replyTo\":{\"fullDatabaseId\":\"4\"}", "\"replyTo\":{\"fullDatabaseId\":\"x\"}")]
    [InlineData("\"pullRequestReview\":{\"fullDatabaseId\":\"2\"}", "\"pullRequestReview\":{\"fullDatabaseId\":null}")]
    public async Task MalformedThreadIsIncompleteAsync(string oldValue, string newValue)
    {
        string thread = Mutate(source: THREAD, oldValue: oldValue, newValue: newValue);

        using RecordingHttpMessageHandler upstream = new(_ => Page(threads: Connection(totalCount: 1, nodes: thread)));

        await this.ReadIncompleteAsync(upstream);
    }

    [Theory]
    [InlineData(COMMENTS_CONNECTION, "null")]
    [InlineData("\"totalCount\":1", "\"totalCount\":null")]
    [InlineData("\"totalCount\":1", "\"totalCount\":-1")]
    [InlineData("\"totalCount\":1", "\"totalCount\":2")]
    [InlineData("\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}", "\"pageInfo\":null")]
    [InlineData("\"hasNextPage\":false", "\"hasNextPage\":null")]
    [InlineData("\"nodes\":[" + COMMENT + "]", "\"nodes\":null")]
    [InlineData("\"nodes\":[" + COMMENT + "]", "\"nodes\":[null]")]
    public async Task MalformedListIsIncompleteAsync(string oldValue, string newValue)
    {
        string comments = Mutate(source: COMMENTS_CONNECTION, oldValue: oldValue, newValue: newValue);

        using RecordingHttpMessageHandler upstream = new(_ => Page(comments: comments));

        await this.ReadIncompleteAsync(upstream);
    }

    [Theory]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":{"repository":{"pullRequest":null}}}""")]
    public async Task MissingPullRequestDataIsIncompleteAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        await this.ReadIncompleteAsync(upstream);
    }

    [Fact]
    public async Task ShortPageFollowedByAnotherIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(comments: Connection(totalCount: 150, nodes: COMMENT, hasNextPage: true, endCursor: "cursor-1"))
        );

        await this.ReadIncompleteAsync(upstream);

        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task TotalThatChangesBetweenPagesIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Page(comments: FullPage(totalCount: 101, endCursor: "cursor-1"))
                : Page(comments: Connection(totalCount: 102, nodes: COMMENT))
        );

        await this.ReadIncompleteAsync(upstream);

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad cursor")]
    public async Task InvalidCursorIsIncompleteAsync(string? cursor)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(comments: FullPage(totalCount: 101, endCursor: cursor))
        );

        await this.ReadIncompleteAsync(upstream);

        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task RepeatedCursorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(comments: FullPage(totalCount: 300, endCursor: "same-cursor"))
        );

        await this.ReadIncompleteAsync(upstream);

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadingStopsAfterOneHundredPagesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Page(
                comments: FullPage(
                    totalCount: 100_000,
                    endCursor: "cursor-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            )
        );

        await this.ReadIncompleteAsync(upstream);

        Assert.Equal(expected: 100, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"errors":[{"message":"Could not resolve to a PullRequest with the number of 41."}],"data":null}"""
            )
        );

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "pull request " + REFERENCE + " was not found", actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task OtherGraphQlErrorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Something went wrong"}],"data":null}""")
        );

        PullRequestCommentsIncompleteException exception = await this.ReadIncompleteAsync(upstream);

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        PullRequestCommentsIncompleteException exception = await this.ReadIncompleteAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        PullRequestCommentsIncompleteException exception = await this.ReadIncompleteAsync(upstream);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page());
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateReader(upstream)
                .ReadAsync(repository: REPOSITORY, pullRequest: PULL_REQUEST, cancellationToken: cancelled.Token)
                .AsTask()
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page());

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateReader(upstream)
                .ReadAsync(
                    repository: repository,
                    pullRequest: PULL_REQUEST,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PullRequestNumberMustBePositiveAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page());

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateReader(upstream)
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: pullRequest,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    private static string Mutate(string source, string oldValue, string newValue)
    {
        // Guards against a typo in the test silently leaving the payload unchanged
        Assert.Contains(expectedSubstring: oldValue, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: oldValue, newValue: newValue, comparisonType: StringComparison.Ordinal);
    }

    private static string Repeat(string node, int count)
    {
        return string.Join(separator: ',', values: Enumerable.Repeat(element: node, count: count));
    }

    private static string FullPage(int totalCount, string? endCursor)
    {
        return Connection(
            totalCount: totalCount,
            nodes: Repeat(node: COMMENT, count: 100),
            hasNextPage: true,
            endCursor: endCursor
        );
    }

    private static string Connection(int totalCount, string nodes, bool hasNextPage = false, string? endCursor = null)
    {
        string cursor = endCursor is null ? "null" : "\"" + endCursor + "\"";

        return "{\"totalCount\":"
            + totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"pageInfo\":{\"hasNextPage\":"
            + (hasNextPage ? "true" : "false")
            + ",\"endCursor\":"
            + cursor
            + "},\"nodes\":["
            + nodes
            + "]}";
    }

    private static HttpResponseMessage Page(
        string comments = EMPTY_CONNECTION,
        string reviews = EMPTY_CONNECTION,
        string threads = EMPTY_CONNECTION,
        string reviewDecision = "\"APPROVED\""
    )
    {
        return Responses.Json(
            "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewDecision\":"
                + reviewDecision
                + ",\"comments\":"
                + comments
                + ",\"reviews\":"
                + reviews
                + ",\"reviewThreads\":"
                + threads
                + "}}}}"
        );
    }

    private async Task<PullRequestCommentsIncompleteException> ReadIncompleteAsync(HttpMessageHandler upstream)
    {
        PullRequestCommentsIncompleteException exception =
            await Assert.ThrowsAsync<PullRequestCommentsIncompleteException>(() => this.ReadAsync(upstream).AsTask());

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);

        return exception;
    }

    private PullRequestCommentsReader CreateReader(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client);
    }

    private ValueTask<PullRequestComments> ReadAsync(HttpMessageHandler upstream)
    {
        return this.CreateReader(upstream)
            .ReadAsync(repository: REPOSITORY, pullRequest: PULL_REQUEST, cancellationToken: this.CancellationToken());
    }
}
