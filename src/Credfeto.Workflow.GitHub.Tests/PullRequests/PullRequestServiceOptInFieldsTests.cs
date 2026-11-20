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
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PullRequestServiceOptInFieldsTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 41;
    private const string INCOMPLETE = "the pull request credfeto/credfeto-workflow-mcp#41 could not be read completely";

    private const string CHECK_RUN =
        """{"__typename":"CheckRun","name":"build","status":"COMPLETED","conclusion":"SUCCESS"}""";
    private const string STATUS = """{"__typename":"StatusContext","context":"ci/legacy","state":"PENDING"}""";

    private static HttpResponseMessage Graph(string node)
    {
        return Responses.Json("""{"data":{"repository":{"pullRequest":""" + node + "}}}");
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static string Count(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string PageInfo(bool hasNext, string? cursor)
    {
        return "\"pageInfo\":{\"hasNextPage\":"
            + (hasNext ? "true" : "false")
            + ",\"endCursor\":"
            + (cursor is null ? "null" : "\"" + cursor + "\"")
            + "}";
    }

    private static string ChecksNode(
        int total,
        IReadOnlyList<string> contexts,
        bool hasNext = false,
        string? cursor = null
    )
    {
        return "{\"headCommit\":{\"nodes\":[{\"commit\":{\"statusCheckRollup\":{\"contexts\":{\"totalCount\":"
            + Count(total)
            + ","
            + PageInfo(hasNext: hasNext, cursor: cursor)
            + ",\"nodes\":["
            + string.Join(separator: ',', values: contexts)
            + "]}}}}]}}";
    }

    private static string Commit(string oid, string authors, int total)
    {
        return "{\"commit\":{\"oid\":\""
            + oid
            + "\",\"authors\":{\"totalCount\":"
            + Count(total)
            + ",\"nodes\":["
            + authors
            + "]}}}";
    }

    private static string CommitsNode(
        int total,
        IReadOnlyList<string> commits,
        bool hasNext = false,
        string? cursor = null
    )
    {
        return "{\"commitIdentities\":{\"totalCount\":"
            + Count(total)
            + ","
            + PageInfo(hasNext: hasNext, cursor: cursor)
            + ",\"nodes\":["
            + string.Join(separator: ',', values: commits)
            + "]}}";
    }

    private PullRequestService CreateService(
        RecordingHttpMessageHandler graphUpstream,
        RecordingHttpMessageHandler restUpstream
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(restUpstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        GitHubGraphQlClient graph = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(graphUpstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: graph, restClient: rest, leakChecker: GetSubstitute<ILeakChecker>());
    }

    private async Task<PullRequestView> ViewAsync(
        RecordingHttpMessageHandler graphUpstream,
        IReadOnlyList<string> fields
    )
    {
        using RecordingHttpMessageHandler noRest = new(Unexpected);

        return await this.CreateService(graphUpstream: graphUpstream, restUpstream: noRest)
            .ViewAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                fields: fields,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<PullRequestView> ViewNodeAsync(string node, string field)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Graph(node));

        return await this.ViewAsync(graphUpstream: upstream, fields: [field]);
    }

    private async Task AssertIncompleteAsync(string node, string field)
    {
        PullRequestViewFailedException exception = await Assert.ThrowsAsync<PullRequestViewFailedException>(() =>
            this.ViewNodeAsync(node: node, field: field)
        );

        Assert.Equal(expected: INCOMPLETE, actual: exception.Message);
    }

    private static JsonElement Variables(RecordedRequest request, out string query)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body ?? "");

        query = document.RootElement.GetProperty("query").GetString() ?? "";

        return document.RootElement.GetProperty("variables").Clone();
    }

    [Theory]
    [InlineData(PullRequestViewFields.ReviewRequests)]
    [InlineData(PullRequestViewFields.Milestone)]
    [InlineData(PullRequestViewFields.CrossRepository)]
    [InlineData(PullRequestViewFields.ClosingIssues)]
    [InlineData(PullRequestViewFields.Checks)]
    [InlineData(PullRequestViewFields.Commits)]
    public static void OptInFieldsAreNotInTheDefaultSet(string field)
    {
        Assert.DoesNotContain(
            expected: field,
            collection: PullRequestViewFields.Default,
            comparer: StringComparer.Ordinal
        );
        Assert.Contains(expected: field, collection: PullRequestViewFields.All, comparer: StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReviewRequestsAreJoinedLoginsAndTeamSlugsAsync()
    {
        PullRequestView view = await this.ViewNodeAsync(
            node: """
            {"reviewRequests":{"totalCount":4,"nodes":[
            {"requestedReviewer":{"__typename":"User","login":"alice"}},
            {"requestedReviewer":{"__typename":"Team","slug":"reviewers"}},
            {"requestedReviewer":null},
            {"requestedReviewer":{"__typename":"Bot","login":"copilot"}}]}}
            """,
            field: PullRequestViewFields.ReviewRequests
        );

        Assert.Equal(expected: "alice,reviewers,copilot", actual: view.ReviewRequests);
        Assert.Null(view.State);
    }

    [Theory]
    [InlineData("""{"reviewRequests":null}""")]
    [InlineData("""{"reviewRequests":{"totalCount":2,"nodes":[{"requestedReviewer":{"login":"alice"}}]}}""")]
    public Task CutShortReviewRequestsAreIncompleteAsync(string node)
    {
        return this.AssertIncompleteAsync(node: node, field: PullRequestViewFields.ReviewRequests);
    }

    [Theory]
    [InlineData("""{"milestone":{"title":"v1\n.0"}}""", "v1 .0")]
    [InlineData("""{"milestone":null}""", "")]
    public async Task MilestoneIsItsTitleAsync(string node, string expected)
    {
        PullRequestView view = await this.ViewNodeAsync(node: node, field: PullRequestViewFields.Milestone);

        Assert.Equal(expected: expected, actual: view.Milestone);
    }

    [Theory]
    [InlineData("""{"isCrossRepository":true}""", "yes")]
    [InlineData("""{"isCrossRepository":false}""", "no")]
    [InlineData("""{"isCrossRepository":null}""", "no")]
    public async Task CrossRepositoryIsYesOrNoAsync(string node, string expected)
    {
        PullRequestView view = await this.ViewNodeAsync(node: node, field: PullRequestViewFields.CrossRepository);

        Assert.Equal(expected: expected, actual: view.CrossRepository);
    }

    [Fact]
    public async Task ClosingIssuesKeepTheirRepositoryAsync()
    {
        PullRequestView view = await this.ViewNodeAsync(
            node: """
            {"closingIssuesReferences":{"totalCount":2,"nodes":[
            {"number":12,"repository":{"nameWithOwner":"credfeto/credfeto-workflow-mcp"}},
            {"number":3,"repository":{"nameWithOwner":"credfeto/other"}}]}}
            """,
            field: PullRequestViewFields.ClosingIssues
        );

        Assert.Equal(
            expected:
            [
                new ClosingIssueReference(Repository: REPOSITORY, Number: 12),
                new ClosingIssueReference(Repository: "credfeto/other", Number: 3),
            ],
            actual: view.ClosingIssues ?? []
        );
    }

    [Theory]
    [InlineData("""{"closingIssuesReferences":null}""")]
    [InlineData("""{"closingIssuesReferences":{"totalCount":1,"nodes":[]}}""")]
    [InlineData("""{"closingIssuesReferences":{"totalCount":1,"nodes":[{"number":1,"repository":null}]}}""")]
    public Task CutShortClosingIssuesAreIncompleteAsync(string node)
    {
        return this.AssertIncompleteAsync(node: node, field: PullRequestViewFields.ClosingIssues);
    }

    [Fact]
    public async Task ChecksListCheckRunsAndStatusContextsAsync()
    {
        string unfinished = """{"__typename":"CheckRun","name":"lint\n","status":"IN_PROGRESS","conclusion":null}""";
        using RecordingHttpMessageHandler upstream = new(_ =>
            Graph(ChecksNode(total: 3, contexts: [CHECK_RUN, STATUS, unfinished]))
        );

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Checks]);

        Assert.Equal(
            expected:
            [
                new PullRequestCheck(
                    Kind: "check-run",
                    Name: "build",
                    Status: "COMPLETED",
                    Conclusion: "SUCCESS",
                    State: ""
                ),
                new PullRequestCheck(Kind: "status", Name: "ci/legacy", Status: "", Conclusion: "", State: "PENDING"),
                new PullRequestCheck(
                    Kind: "check-run",
                    Name: "lint ",
                    Status: "IN_PROGRESS",
                    Conclusion: "",
                    State: ""
                ),
            ],
            actual: view.Checks ?? []
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        JsonElement variables = Variables(request: request, query: out string query);
        Assert.StartsWith(
            expectedStartString: "query($o:String!,$r:String!,$n:Int!,$checksAfter:String){",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "headCommit:commits(last:1)",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
        Assert.False(
            condition: variables.TryGetProperty(propertyName: "checksAfter", value: out _),
            userMessage: "The first page should not send a cursor"
        );
    }

    [Theory]
    [InlineData("""{"headCommit":{"nodes":[]}}""")]
    [InlineData("""{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}""")]
    public async Task NoRollupIsNoChecksAsync(string node)
    {
        PullRequestView view = await this.ViewNodeAsync(node: node, field: PullRequestViewFields.Checks);

        Assert.Empty(view.Checks ?? throw new InvalidOperationException("checks were not read"));
    }

    [Fact]
    public async Task ChecksArePagedAsync()
    {
        string[] firstPage = [.. Enumerable.Repeat(element: CHECK_RUN, count: 100)];
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Graph(ChecksNode(total: 101, contexts: firstPage, hasNext: true, cursor: "Y2hlY2tzOjEwMA=="))
                : Graph(ChecksNode(total: 101, contexts: [STATUS]))
        );

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.State, PullRequestViewFields.Checks]
        );

        Assert.Equal(expected: 101, actual: view.Checks?.Count);
        Assert.Equal(expected: "status", actual: view.Checks?[100].Kind);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        JsonElement second = Variables(request: upstream.Requests[1], query: out string secondQuery);
        Assert.Equal(expected: "Y2hlY2tzOjEwMA==", actual: second.GetProperty("checksAfter").GetString());
        Assert.False(
            condition: second.TryGetProperty(propertyName: "after", value: out _),
            userMessage: "The checks page should not send a commits cursor"
        );
        Assert.StartsWith(
            expectedStartString: "query($o:String!,$r:String!,$n:Int!,$checksAfter:String){repository(owner:$o,name:$r){pullRequest(number:$n){headCommit:commits(last:1)",
            actualString: secondQuery,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "{state ",
            actualString: secondQuery,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("""{"headCommit":null}""")]
    [InlineData("""{"headCommit":{"nodes":null}}""")]
    [InlineData("""{"headCommit":{"nodes":[null]}}""")]
    [InlineData("""{"headCommit":{"nodes":[{"commit":null}]}}""")]
    [InlineData("""{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":null}}}]}}""")]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":2,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"__typename":"CheckRun","name":"a"}]}}}}]}}"""
    )]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[null]}}}}]}}"""
    )]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"__typename":"CheckRun"}]}}}}]}}"""
    )]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"__typename":"StatusContext","state":"SUCCESS"}]}}}}]}}"""
    )]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"__typename":"Other","name":"a"}]}}}}]}}"""
    )]
    [InlineData(
        """{"headCommit":{"nodes":[{"commit":{"statusCheckRollup":{"contexts":{"totalCount":101,"pageInfo":{"hasNextPage":true,"endCursor":"bad cursor"},"nodes":[]}}}}]}}"""
    )]
    public Task MalformedChecksAreIncompleteAsync(string node)
    {
        return this.AssertIncompleteAsync(node: node, field: PullRequestViewFields.Checks);
    }

    [Fact]
    public async Task CommitsListEveryAuthorWithEmailAsync()
    {
        string first = Commit(
            oid: "abc",
            authors: """{"user":{"login":"alice"},"email":"alice@example.com","name":"Alice"},{"user":null,"email":"bot@example.com","name":"Bot"}""",
            total: 2
        );
        string second = Commit(oid: "def", authors: """{"user":{"login":"bob"},"email":null,"name":null}""", total: 1);
        using RecordingHttpMessageHandler upstream = new(_ => Graph(CommitsNode(total: 2, commits: [first, second])));

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Commits]);

        IReadOnlyList<PullRequestCommitIdentity> commits = view.Commits ?? [];
        Assert.Equal(expected: 2, actual: commits.Count);
        Assert.Equal(expected: "abc", actual: commits[0].Oid);
        Assert.Equal(
            expected:
            [
                new CommitAuthorIdentity(Login: "alice", Email: "alice@example.com", Name: "Alice"),
                new CommitAuthorIdentity(Login: "", Email: "bot@example.com", Name: "Bot"),
            ],
            actual: commits[0].Authors
        );
        Assert.Equal(
            expected: [new CommitAuthorIdentity(Login: "bob", Email: "", Name: "")],
            actual: commits[1].Authors
        );
        Assert.Null(view.CommitAuthors);
        _ = Variables(request: Assert.Single(upstream.Requests), query: out string query);
        Assert.Contains(
            expectedSubstring: "commitIdentities:commits(first:100,after:$after)",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CommitsArePagedAsync()
    {
        string commit = Commit(
            oid: "abc",
            authors: """{"user":{"login":"alice"},"email":"a@example.com","name":"A"}""",
            total: 1
        );
        string[] firstPage = [.. Enumerable.Repeat(element: commit, count: 100)];
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Graph(CommitsNode(total: 101, commits: firstPage, hasNext: true, cursor: "Y29tbWl0czoxMDA="))
                : Graph(CommitsNode(total: 101, commits: [commit]))
        );

        PullRequestView view = await this.ViewAsync(graphUpstream: upstream, fields: [PullRequestViewFields.Commits]);

        Assert.Equal(expected: 101, actual: view.Commits?.Count);
        JsonElement second = Variables(request: upstream.Requests[1], query: out string secondQuery);
        Assert.Equal(expected: "Y29tbWl0czoxMDA=", actual: second.GetProperty("after").GetString());
        Assert.StartsWith(
            expectedStartString: "query($o:String!,$r:String!,$n:Int!,$after:String){repository(owner:$o,name:$r){pullRequest(number:$n){commitIdentities:commits(first:100,after:$after)",
            actualString: secondQuery,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CommitsAndCommitAuthorsCanBeReadTogetherAsync()
    {
        string node = """
            {"commits":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
            {"commit":{"authors":{"totalCount":1,"nodes":[{"user":{"login":"alice"}}]}}}]},
            "commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
            {"commit":{"oid":"abc","authors":{"totalCount":1,"nodes":[{"user":{"login":"alice"},"email":"a@example.com","name":"A"}]}}}]},
            "headCommit":{"nodes":[]}}
            """;

        using RecordingHttpMessageHandler upstream = new(_ => Graph(node));

        PullRequestView view = await this.ViewAsync(
            graphUpstream: upstream,
            fields: [PullRequestViewFields.CommitAuthors, PullRequestViewFields.Commits, PullRequestViewFields.Checks]
        );

        Assert.Equal(expected: "alice", actual: view.CommitAuthors);
        Assert.Equal(expected: "a@example.com", actual: Assert.Single(Assert.Single(view.Commits ?? []).Authors).Email);
        Assert.Empty(view.Checks ?? throw new InvalidOperationException("checks were not read"));
        _ = Variables(request: Assert.Single(upstream.Requests), query: out string query);
        Assert.StartsWith(
            expectedStartString: "query($o:String!,$r:String!,$n:Int!,$after:String,$checksAfter:String){",
            actualString: query,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("""{"commitIdentities":null}""")]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[null]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"commit":null}]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"commit":{"oid":"","authors":{"totalCount":0,"nodes":[]}}}]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"commit":{"oid":"abc","authors":null}}]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"commit":{"oid":"abc","authors":{"totalCount":2,"nodes":[{"user":null,"email":"a@example.com","name":"A"}]}}}]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":1,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[{"commit":{"oid":"abc","authors":{"totalCount":1,"nodes":[null]}}}]}}"""
    )]
    [InlineData(
        """{"commitIdentities":{"totalCount":2,"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[]}}"""
    )]
    public Task MalformedCommitsAreIncompleteAsync(string node)
    {
        return this.AssertIncompleteAsync(node: node, field: PullRequestViewFields.Commits);
    }
}
