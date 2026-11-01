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
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PullRequestServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;
    private const string INCOMPLETE = "the pull request credfeto/credfeto-workflow-mcp#41 could not be read completely";

    private const string FILES_URI = GitHubServices.ApiBaseUrl + "repos/credfeto/credfeto-workflow-mcp/pulls/41/files";

    private const string FULL_NODE = """
        {"state":"OPEN","isDraft":true,"title":"Add view","url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/41",
        "baseRefName":"main","headRefName":"feat/26-pr-view-tool","headRefOid":"abc123",
        "autoMergeRequest":{"enabledAt":"2026-01-01T00:00:00Z"},"reviewDecision":"APPROVED","mergeStateStatus":"CLEAN",
        "mergeable":"MERGEABLE","createdAt":"2026-01-01T00:00:00Z","merged":true,"mergedAt":"2026-01-02T00:00:00Z",
        "mergeCommit":{"oid":"def456"},
        "labels":{"totalCount":2,"nodes":[{"name":"bug"},null,{"name":null},{"name":"feature"}]},
        "assignees":{"totalCount":1,"nodes":[{"login":"credfeto"}]},"author":{"login":"credfeto"},
        "commits":{"totalCount":2,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
        {"commit":{"authors":{"totalCount":2,"nodes":[{"user":{"login":"alice"}},{"user":null}]}}},
        {"commit":{"authors":{"totalCount":2,"nodes":[{"user":{"login":"bob"}},{"user":{"login":"alice"}}]}}}]},
        "changedFiles":2,"body":"Line one\nLine two"}
        """;

    private static string Wrap(string node)
    {
        return """{"data":{"repository":{"pullRequest":""" + node + "}}}";
    }

    private static string Mutate(string source, string from, string to)
    {
        Assert.Contains(expectedSubstring: from, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: from, newValue: to, comparisonType: StringComparison.Ordinal);
    }

    private static string CommitsNode(int total, int count, bool hasNext, string? cursor, string login = "alice")
    {
        string commit = """{"commit":{"authors":{"totalCount":1,"nodes":[{"user":{"login":"LOGIN"}}]}}}""".Replace(
            oldValue: "LOGIN",
            newValue: login,
            comparisonType: StringComparison.Ordinal
        );
        string nodes = string.Join(separator: ',', values: Enumerable.Repeat(element: commit, count: count));
        string endCursor = cursor is null ? "null" : "\"" + cursor + "\"";
        string next = hasNext ? "true" : "false";

        return "{\"commits\":{\"totalCount\":"
            + total.ToString(CultureInfo.InvariantCulture)
            + ",\"pageInfo\":{\"hasNextPage\":"
            + next
            + ",\"endCursor\":"
            + endCursor
            + "},\"nodes\":["
            + nodes
            + "]}}";
    }

    private static HttpResponseMessage Graph(string node)
    {
        return Responses.Json(Wrap(node));
    }

    private static HttpResponseMessage FilesPage(string json, string? next = null)
    {
        return Responses.Json(json: json, nextLink: next is null ? null : "<" + next + ">; rel=\"next\"");
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private PullRequestService CreateService(
        RecordingHttpMessageHandler graphUpstream,
        RecordingHttpMessageHandler restUpstream
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(restUpstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        GitHubGraphQlClient graph = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(graphUpstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: graph, restClient: rest);
    }

    private async Task<PullRequestView> ViewAsync(
        RecordingHttpMessageHandler graphUpstream,
        IReadOnlyList<string>? fields,
        RecordingHttpMessageHandler? restUpstream = null
    )
    {
        using RecordingHttpMessageHandler noRest = new(Unexpected);

        return await this.CreateService(graphUpstream: graphUpstream, restUpstream: restUpstream ?? noRest)
            .ViewAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                fields: fields,
                cancellationToken: this.CancellationToken()
            );
    }

    private Task<PullRequestViewFailedException> ViewFailsAsync(
        RecordingHttpMessageHandler graphUpstream,
        IReadOnlyList<string>? fields,
        RecordingHttpMessageHandler? restUpstream = null
    )
    {
        return Assert.ThrowsAsync<PullRequestViewFailedException>(() =>
            this.ViewAsync(graphUpstream: graphUpstream, fields: fields, restUpstream: restUpstream)
        );
    }

    private async Task AssertIncompleteAsync(string node, IReadOnlyList<string>? fields)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Graph(node));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(graphUpstream: upstream, fields: fields);

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    private async Task AssertRejectedAsync(string repository, int pullRequest, IReadOnlyList<string>? fields)
    {
        using RecordingHttpMessageHandler graphUpstream = new(Unexpected);
        using RecordingHttpMessageHandler restUpstream = new(Unexpected);

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(graphUpstream: graphUpstream, restUpstream: restUpstream)
                .ViewAsync(
                    repository: repository,
                    pullRequest: pullRequest,
                    fields: fields,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(graphUpstream.Requests);
        Assert.Empty(restUpstream.Requests);
    }

    private static JsonElement Variables(RecordedRequest request, out string query)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body ?? "");

        query = document.RootElement.GetProperty("query").GetString() ?? "";

        return document.RootElement.GetProperty("variables").Clone();
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        return this.AssertRejectedAsync(repository: repository, pullRequest: PULL_REQUEST, fields: null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task NonPositivePullRequestIsRejectedBeforeAnyRequestAsync(int pullRequest)
    {
        return this.AssertRejectedAsync(repository: REPOSITORY, pullRequest: pullRequest, fields: null);
    }

    [Fact]
    public Task UnknownFieldIsRejectedBeforeAnyRequestAsync()
    {
        return this.AssertRejectedAsync(repository: REPOSITORY, pullRequest: PULL_REQUEST, fields: ["state", "colour"]);
    }

    [Fact]
    public Task NullFieldIsRejectedBeforeAnyRequestAsync()
    {
        return this.AssertRejectedAsync(repository: REPOSITORY, pullRequest: PULL_REQUEST, fields: ["state", null!]);
    }

    [Theory]
    [InlineData(PullRequestViewFields.Files)]
    [InlineData(PullRequestViewFields.Body)]
    public Task FilesOrBodyWithAnotherFieldIsRejectedBeforeAnyRequestAsync(string field)
    {
        return this.AssertRejectedAsync(
            repository: REPOSITORY,
            pullRequest: PULL_REQUEST,
            fields: [field, PullRequestViewFields.State]
        );
    }

    [Fact]
    public void DefaultFieldsAreEverythingExceptFilesAndBody()
    {
        string[] expected =
        [
            .. PullRequestViewFields.All.Where(static field =>
                field is not PullRequestViewFields.Files and not PullRequestViewFields.Body
            ),
        ];

        Assert.Equal(expected: expected, actual: PullRequestViewFields.Default, comparer: StringComparer.Ordinal);
    }

    [Fact]
    public async Task DefaultFieldsAreQueriedWithAllVariablesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph(FULL_NODE));

        _ = await this.ViewAsync(graphUpstream: upstream, fields: null);

        RecordedRequest request = Assert.Single(upstream.Requests);
        JsonElement variables = Variables(request: request, query: out string query);

        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: PULL_REQUEST, actual: variables.GetProperty("n").GetInt32());
        Assert.False(
            condition: variables.TryGetProperty(propertyName: "after", value: out _),
            userMessage: "A single query should not send a cursor"
        );
        Assert.StartsWith(
            expectedStartString: "query($o:String!,$r:String!,$n:Int!,$after:String){repository(owner:$o,name:$r){pullRequest(number:$n){",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(expectedSubstring: "isDraft", actualString: query, comparisonType: StringComparison.Ordinal);
        Assert.Contains(
            expectedSubstring: "autoMergeRequest{enabledAt}",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "changedFiles",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(expectedSubstring: "body", actualString: query, comparisonType: StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryWithoutCommitAuthorsHasNoCursorVariableAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph("""{"title":"Add view"}"""));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Title]);

        Assert.Equal(expected: "Add view", actual: view.Title);
        RecordedRequest request = Assert.Single(upstream.Requests);
        _ = Variables(request: request, query: out string query);
        Assert.Equal(
            expected: "query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){title}}}",
            actual: query
        );
    }

    [Fact]
    public async Task DuplicateFieldsAreReturnedOnceInCanonicalOrderAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph(FULL_NODE));

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.Title, PullRequestViewFields.State, PullRequestViewFields.Title]
        );

        Assert.Equal(expected: "OPEN", actual: view.State);
        Assert.Equal(expected: "Add view", actual: view.Title);
        Assert.Null(view.Draft);
        RecordedRequest request = Assert.Single(upstream.Requests);
        _ = Variables(request: request, query: out string query);
        Assert.EndsWith(
            expectedEndString: "{pullRequest(number:$n){state title}}}",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task DefaultFieldsAreMappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph(FULL_NODE));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: null);

        Assert.Equal(expected: REPOSITORY + "#41", actual: view.Pr);
        Assert.Equal(expected: "OPEN", actual: view.State);
        Assert.Equal(expected: "yes", actual: view.Draft);
        Assert.Equal(expected: "Add view", actual: view.Title);
        Assert.Equal(expected: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/41"), actual: view.Url);
        Assert.Equal(expected: "main", actual: view.Base);
        Assert.Equal(expected: "feat/26-pr-view-tool", actual: view.Head);
        Assert.Equal(expected: "abc123", actual: view.HeadSha);
        Assert.Equal(expected: "on", actual: view.AutoMerge);
        Assert.Equal(expected: "APPROVED", actual: view.ReviewDecision);
        Assert.Equal(expected: "CLEAN", actual: view.MergeState);
        Assert.Equal(expected: "MERGEABLE", actual: view.Mergeable);
        Assert.Equal(expected: "2026-01-01T00:00:00Z", actual: view.CreatedAt);
        Assert.Equal(expected: "2026-01-02T00:00:00Z", actual: view.MergedAt);
        Assert.Equal(expected: "def456", actual: view.MergeCommit);
        Assert.Equal(expected: "bug,feature", actual: view.Labels);
        Assert.Equal(expected: "credfeto", actual: view.Assignees);
        Assert.Equal(expected: "credfeto", actual: view.Author);
        Assert.Equal(expected: "alice,bob", actual: view.CommitAuthors);
        Assert.Null(view.Files);
        Assert.Null(view.Body);
    }

    [Fact]
    public async Task UnmergedNotDraftWithoutAutoMergeIsMappedAsync()
    {
        string node = Mutate(
            source: Mutate(
                source: Mutate(source: FULL_NODE, from: "\"merged\":true", to: "\"merged\":false"),
                from: "\"isDraft\":true",
                to: "\"isDraft\":null"
            ),
            from: "{\"enabledAt\":\"2026-01-01T00:00:00Z\"}",
            to: "null"
        );
        using RecordingHttpMessageHandler upstream = new(_ => Graph(node));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: null);

        Assert.Equal(expected: "no", actual: view.Draft);
        Assert.Equal(expected: "off", actual: view.AutoMerge);
        Assert.Equal(expected: "", actual: view.MergedAt);
        Assert.Equal(expected: "", actual: view.MergeCommit);
    }

    [Fact]
    public async Task ControlCharactersAreReplacedAndMissingValuesAreEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Graph("""{"state":null,"title":"Add\nview\u0007","author":null}""")
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.State, PullRequestViewFields.Title, PullRequestViewFields.Author]
        );

        Assert.Equal(expected: "", actual: view.State);
        Assert.Equal(expected: "Add view ", actual: view.Title);
        Assert.Equal(expected: "", actual: view.Author);
    }

    [Theory]
    [InlineData("\"url\":\"not a url\"")]
    [InlineData("\"url\":null")]
    public Task InvalidUrlIsIncompleteAsync(string url)
    {
        return this.AssertIncompleteAsync(node: "{" + url + "}", fields: [PullRequestViewFields.Url]);
    }

    [Fact]
    public async Task BodyIsReturnedUncleanedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph(FULL_NODE));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Body]);

        Assert.Equal(expected: "Line one\nLine two", actual: view.Body);
        Assert.Null(view.State);
    }

    [Fact]
    public async Task MissingBodyIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Graph("""{"body":null}"""));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Body]);

        Assert.Equal(expected: "", actual: view.Body);
    }

    [Theory]
    [InlineData("""{"labels":{"totalCount":3,"nodes":[{"name":"bug"}]}}""", PullRequestViewFields.Labels)]
    [InlineData("""{"labels":{"nodes":[{"name":"bug"}]}}""", PullRequestViewFields.Labels)]
    [InlineData("""{"labels":{"totalCount":1}}""", PullRequestViewFields.Labels)]
    [InlineData("""{"labels":null}""", PullRequestViewFields.Labels)]
    [InlineData("""{"assignees":{"totalCount":2,"nodes":[{"login":"credfeto"}]}}""", PullRequestViewFields.Assignees)]
    public Task TruncatedConnectionIsIncompleteAsync(string node, string field)
    {
        return this.AssertIncompleteAsync(node: node, fields: [field]);
    }

    [Theory]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":{"repository":{"pullRequest":null}}}""")]
    public async Task MissingPullRequestIsIncompleteAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(graphUpstream: upstream, fields: null);

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    [Fact]
    public async Task UnknownPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"errors":[{"message":"Could not resolve to a PullRequest with the number of 41."}],"data":null}"""
            )
        );

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ViewAsync(graphUpstream: upstream, fields: null)
        );

        Assert.Equal(
            expected: "pull request credfeto/credfeto-workflow-mcp#41 was not found",
            actual: exception.Message
        );
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task GraphQlErrorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Something went wrong"}],"data":null}""")
        );

        PullRequestViewFailedException exception = await this.ViewFailsAsync(graphUpstream: upstream, fields: null);

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(graphUpstream: upstream, fields: null);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(graphUpstream: upstream, fields: null);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph(FULL_NODE));
        using RecordingHttpMessageHandler restUpstream = new(Unexpected);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateService(graphUpstream: graphUpstream, restUpstream: restUpstream)
                .ViewAsync(
                    repository: REPOSITORY,
                    pullRequest: PULL_REQUEST,
                    fields: null,
                    cancellationToken: cancelled.Token
                )
                .AsTask()
        );
    }

    [Fact]
    public async Task CommitAuthorsArePagedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Graph(CommitsNode(total: 101, count: 100, hasNext: true, cursor: "Y3Vyc29yOjEwMA=="))
                : Graph(CommitsNode(total: 101, count: 1, hasNext: false, cursor: null, login: "bob"))
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: "alice,bob", actual: view.CommitAuthors);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        JsonElement first = Variables(request: upstream.Requests[0], query: out string firstQuery);
        JsonElement second = Variables(request: upstream.Requests[1], query: out string secondQuery);
        Assert.False(
            condition: first.TryGetProperty(propertyName: "after", value: out _),
            userMessage: "The first page should not send a cursor"
        );
        Assert.Equal(expected: "Y3Vyc29yOjEwMA==", actual: second.GetProperty("after").GetString());
        Assert.Equal(expected: firstQuery, actual: secondQuery);
        Assert.Contains(
            expectedSubstring: "commits(first:100,after:$after)",
            actualString: secondQuery,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CommitAuthorsAreCappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Graph(
                CommitsNode(
                    total: 20000,
                    count: 100,
                    hasNext: true,
                    cursor: "c" + index.ToString(CultureInfo.InvariantCulture)
                )
            )
        );

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        Assert.Equal(expected: 100, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task CommitAuthorsCompletingOnTheLastAllowedPageAreReturnedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index < 99
                ? Graph(
                    CommitsNode(
                        total: 9901,
                        count: 100,
                        hasNext: true,
                        cursor: "c" + index.ToString(CultureInfo.InvariantCulture)
                    )
                )
                : Graph(CommitsNode(total: 9901, count: 1, hasNext: false, cursor: null))
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: "alice", actual: view.CommitAuthors);
        Assert.Equal(expected: 100, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x\\n")]
    [InlineData("")]
    public Task InvalidCommitCursorIsIncompleteAsync(string cursor)
    {
        return this.AssertIncompleteAsync(
            node: CommitsNode(total: 200, count: 100, hasNext: true, cursor: cursor),
            fields: [PullRequestViewFields.CommitAuthors]
        );
    }

    [Fact]
    public Task MissingCommitCursorIsIncompleteAsync()
    {
        return this.AssertIncompleteAsync(
            node: CommitsNode(total: 200, count: 100, hasNext: true, cursor: null),
            fields: [PullRequestViewFields.CommitAuthors]
        );
    }

    [Fact]
    public async Task RepeatedCommitCursorIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Graph(CommitsNode(total: 300, count: 100, hasNext: true, cursor: "same"))
        );

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ChangingCommitTotalIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Graph(CommitsNode(total: 101, count: 100, hasNext: true, cursor: "next"))
                : Graph(CommitsNode(total: 102, count: 2, hasNext: false, cursor: null))
        );

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    [Theory]
    [InlineData(200, 99, true)]
    [InlineData(3, 2, false)]
    [InlineData(1, 2, false)]
    [InlineData(-1, 0, false)]
    public Task MismatchedCommitPageIsIncompleteAsync(int total, int count, bool hasNext)
    {
        return this.AssertIncompleteAsync(
            node: CommitsNode(total: total, count: count, hasNext: hasNext, cursor: hasNext ? "next" : null),
            fields: [PullRequestViewFields.CommitAuthors]
        );
    }

    [Theory]
    [InlineData("""{"commits":null}""")]
    [InlineData("""{"commits":{"pageInfo":{"hasNextPage":false},"nodes":[]}}""")]
    [InlineData("""{"commits":{"totalCount":0,"pageInfo":{"hasNextPage":null},"nodes":[]}}""")]
    [InlineData("""{"commits":{"totalCount":0,"pageInfo":null,"nodes":[]}}""")]
    [InlineData("""{"commits":{"totalCount":0,"pageInfo":{"hasNextPage":false},"nodes":null}}""")]
    [InlineData("""{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[null]}}""")]
    [InlineData("""{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[{"commit":null}]}}""")]
    [InlineData(
        """{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[{"commit":{"authors":null}}]}}"""
    )]
    [InlineData(
        """{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[{"commit":{"authors":{"nodes":[]}}}]}}"""
    )]
    [InlineData(
        """{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[{"commit":{"authors":{"totalCount":0}}}]}}"""
    )]
    [InlineData(
        """{"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false},"nodes":[{"commit":{"authors":{"totalCount":2,"nodes":[{"user":{"login":"alice"}}]}}}]}}"""
    )]
    public Task MalformedCommitAuthorsAreIncompleteAsync(string node)
    {
        return this.AssertIncompleteAsync(node: node, fields: [PullRequestViewFields.CommitAuthors]);
    }

    [Fact]
    public async Task CommitAuthorLoginsAreCleanedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Graph(CommitsNode(total: 1, count: 1, hasNext: false, cursor: null, login: "ali\\u0007ce"))
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors]
        );

        Assert.Equal(expected: "ali ce", actual: view.CommitAuthors);
    }

    [Fact]
    public async Task FilesArePagedAsync()
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph("""{"changedFiles":3}"""));
        using RecordingHttpMessageHandler restUpstream = new(static index =>
            index == 0
                ? FilesPage(
                    json: """[{"filename":"a.cs"},{"filename":"b\tc.cs"}]""",
                    next: FILES_URI + "?per_page=100&page=2"
                )
                : FilesPage("""[{"filename":"d.cs"}]""")
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        string[] expected = ["a.cs", "b c.cs", "d.cs"];
        Assert.Equal(expected: expected, actual: view.Files);
        Assert.Null(view.State);
        Assert.Equal(expected: 2, actual: restUpstream.Requests.Count);
        Assert.Equal(expected: new Uri(FILES_URI + "?per_page=100"), actual: restUpstream.Requests[0].RequestUri);
        RecordedRequest request = Assert.Single(graphUpstream.Requests);
        _ = Variables(request: request, query: out string query);
        Assert.EndsWith(
            expectedEndString: "{pullRequest(number:$n){changedFiles}}}",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task NoChangedFilesIsEmptyAsync()
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph("""{"changedFiles":0}"""));
        using RecordingHttpMessageHandler restUpstream = new(static _ => FilesPage("[]"));

        PullRequestView view = await this.ViewAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        Assert.NotNull(view.Files);
        Assert.Empty(view.Files);
    }

    [Fact]
    public async Task FewerFilesThanChangedIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph("""{"changedFiles":3001}"""));
        using RecordingHttpMessageHandler restUpstream = new(static _ => FilesPage("""[{"filename":"a.cs"}]"""));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        Assert.Equal(
            expected: "the pull request credfeto/credfeto-workflow-mcp#41 changes 3001 files and listed only 1",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData("""[{"filename":"a.cs"},null]""")]
    [InlineData("""[{"filename":null}]""")]
    [InlineData("""[{}]""")]
    public async Task MalformedFileIsIncompleteAsync(string json)
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph("""{"changedFiles":1}"""));
        using RecordingHttpMessageHandler restUpstream = new(_ => FilesPage(json));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    [Theory]
    [InlineData("""{"changedFiles":null}""")]
    [InlineData("""{"changedFiles":-1}""")]
    public async Task InvalidChangedFilesIsIncompleteBeforeListingAsync(string node)
    {
        using RecordingHttpMessageHandler graphUpstream = new(_ => Graph(node));
        using RecordingHttpMessageHandler restUpstream = new(Unexpected);

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        Assert.Empty(restUpstream.Requests);
    }

    [Fact]
    public async Task FileListingFailureIsIncompleteAsync()
    {
        using RecordingHttpMessageHandler graphUpstream = new(static _ => Graph("""{"changedFiles":1}"""));
        using RecordingHttpMessageHandler restUpstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        PullRequestViewFailedException exception = await this.ViewFailsAsync(
            graphUpstream: graphUpstream,
            fields: [PullRequestViewFields.Files],
            restUpstream: restUpstream
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
