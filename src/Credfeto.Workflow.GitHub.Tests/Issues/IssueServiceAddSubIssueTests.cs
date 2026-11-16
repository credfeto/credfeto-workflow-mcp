using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
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

public sealed class IssueServiceAddSubIssueTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PARENT = 16;
    private const int CHILD = 17;
    private const string PARENT_REFERENCE = "credfeto/credfeto-workflow-mcp#16";
    private const string CHILD_REFERENCE = "credfeto/credfeto-workflow-mcp#17";

    private const string FAILED_MESSAGE =
        "could not add issue credfeto/credfeto-workflow-mcp#17 as a sub-issue of credfeto/credfeto-workflow-mcp#16";

    private const string PARENT_ISSUE = """
        {"id":1016,"title":"Parent","state":"open","labels":[],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/16"}
        """;

    private const string CHILD_ISSUE = """
        {"id":1017,"title":"Child","state":"open","labels":[],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/17"}
        """;

    private const string CHILD_WITHOUT_ID = """
        {"title":"Child","state":"open","labels":[],"assignees":[],"body":"Body",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/17"}
        """;

    private const string CHILD_WITHOUT_LINK = """
        {"id":1017,"title":"Child","state":"open","labels":[],"assignees":[],"body":"Body"}
        """;

    private const string PARENT_PULL_REQUEST = """
        {"id":1016,"title":"Parent","state":"open","labels":[],"assignees":[],"body":"Body",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/16"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/16"}
        """;

    private const string CHILD_PULL_REQUEST = """
        {"id":1017,"title":"Child","state":"open","labels":[],"assignees":[],"body":"Body",
        "pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/17"},
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/17"}
        """;

    private const string NO_SUB_ISSUES = "[]";

    private const string OTHER_SUB_ISSUE = """
        [{"id":1020,"title":"Other","state":"open",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/20"}]
        """;

    private const string CHILD_ALREADY_LINKED = """
        [{"id":1020,"title":"Other","state":"open",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/20"},
        {"id":1017,"title":"Child","state":"open",
        "html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/17"}]
        """;

    private const string SUB_ISSUE_WITHOUT_ID = """
        [{"title":"Other","state":"open","html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/20"}]
        """;

    private static readonly Uri ParentUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/16");

    private static readonly Uri ChildUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/17");

    private static readonly Uri SubIssuesUri = new(ParentUri + "/sub_issues");

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage LinkFlow(int index, string child, string subIssues)
    {
        return index switch
        {
            0 => Responses.Json(PARENT_ISSUE),
            1 => Responses.Json(child),
            2 => Responses.Json(subIssues),
            3 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: PARENT_ISSUE),
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

    private Task<IssueAddSubIssueResponse> AddSubIssueAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int issue = PARENT,
        int subIssue = CHILD
    )
    {
        return this.CreateService(upstream)
            .AddSubIssueAsync(
                repository: repository,
                issue: issue,
                subIssue: subIssue,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    [Theory]
    [InlineData(NO_SUB_ISSUES)]
    [InlineData(OTHER_SUB_ISSUE)]
    public async Task AddLinksTheSubIssueAsync(string subIssues)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            LinkFlow(index: index, child: CHILD_ISSUE, subIssues: subIssues)
        );

        IssueAddSubIssueResponse result = await this.AddSubIssueAsync(upstream);

        Assert.Equal(
            expected: new IssueAddSubIssueResponse(
                Parent: PARENT_REFERENCE,
                SubIssue: CHILD_REFERENCE,
                Outcome: "linked",
                ParentUrl: new("https://github.com/credfeto/credfeto-workflow-mcp/issues/16"),
                SubIssueUrl: new("https://github.com/credfeto/credfeto-workflow-mcp/issues/17")
            ),
            actual: result
        );
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: ParentUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: ChildUri, actual: upstream.Requests[1].RequestUri);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[2].Method);
        Assert.Equal(expected: new Uri(SubIssuesUri + "?per_page=100"), actual: upstream.Requests[2].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[3].Method);
        Assert.Equal(expected: SubIssuesUri, actual: upstream.Requests[3].RequestUri);
        Assert.Equal(expected: """{"sub_issue_id":1017}""", actual: upstream.Requests[3].Body);
    }

    [Fact]
    public async Task AddLeavesAnAlreadyLinkedSubIssueUnchangedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            LinkFlow(index: index, child: CHILD_ISSUE, subIssues: CHILD_ALREADY_LINKED)
        );

        IssueAddSubIssueResponse result = await this.AddSubIssueAsync(upstream);

        Assert.Equal(expected: "already-linked", actual: result.Outcome);
        Assert.Equal(expected: PARENT_REFERENCE, actual: result.Parent);
        Assert.Equal(expected: CHILD_REFERENCE, actual: result.SubIssue);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.DoesNotContain(collection: upstream.Requests, filter: request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AddMissingParentIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: "issue " + PARENT_REFERENCE + " was not found", actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AddMissingSubIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(PARENT_ISSUE),
                1 => Responses.Status(HttpStatusCode.NotFound),
                _ => Unexpected(index),
            }
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: "issue " + CHILD_REFERENCE + " was not found", actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AddPullRequestParentIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Json(PARENT_PULL_REQUEST) : Unexpected(index)
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: "issue " + PARENT_REFERENCE + " was not found", actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AddPullRequestSubIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            LinkFlow(index: index, child: CHILD_PULL_REQUEST, subIssues: NO_SUB_ISSUES)
        );

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: "issue " + CHILD_REFERENCE + " was not found", actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AddAnIssueAsItsOwnSubIssueIsRefusedBeforeAnyRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        IssueAddSubIssueRefusedException exception = await Assert.ThrowsAsync<IssueAddSubIssueRefusedException>(() =>
            this.AddSubIssueAsync(upstream: upstream, subIssue: PARENT)
        );

        Assert.Equal(expected: "an issue cannot be its own sub-issue", actual: exception.Rule);
        Assert.Equal(
            expected: "adding issue "
                + PARENT_REFERENCE
                + " as a sub-issue of "
                + PARENT_REFERENCE
                + " was refused: an issue cannot be its own sub-issue",
            actual: exception.Message
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("not-a-repository", PARENT, CHILD)]
    [InlineData(REPOSITORY, 0, CHILD)]
    [InlineData(REPOSITORY, -1, CHILD)]
    [InlineData(REPOSITORY, PARENT, 0)]
    [InlineData(REPOSITORY, PARENT, -1)]
    [InlineData(REPOSITORY, 0, 0)]
    public async Task AddRejectsInvalidInputAsync(string repository, int issue, int subIssue)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.AddSubIssueAsync(upstream: upstream, repository: repository, issue: issue, subIssue: subIssue)
        );

        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AddParentReadFailureIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AddSubIssueReadFailureIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(PARENT_ISSUE),
                1 => Responses.Html("<html></html>"),
                _ => Unexpected(index),
            }
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AddSubIssueListFailureIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(PARENT_ISSUE),
                1 => Responses.Json(CHILD_ISSUE),
                2 => Responses.Status(HttpStatusCode.BadGateway),
                _ => Unexpected(index),
            }
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AddRejectedLinkIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json(PARENT_ISSUE),
                1 => Responses.Json(CHILD_ISSUE),
                2 => Responses.Json(NO_SUB_ISSUES),
                3 => Responses.Status(HttpStatusCode.UnprocessableEntity),
                _ => Unexpected(index),
            }
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(CHILD_WITHOUT_ID, NO_SUB_ISSUES)]
    [InlineData(CHILD_WITHOUT_LINK, NO_SUB_ISSUES)]
    public async Task AddUnusableSubIssueIsWrappedBeforeAnyWriteAsync(string child, string subIssues)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            LinkFlow(index: index, child: child, subIssues: subIssues)
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AddSubIssueListEntryWithoutAnIdIsWrappedBeforeAnyWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            LinkFlow(index: index, child: CHILD_ISSUE, subIssues: SUB_ISSUE_WITHOUT_ID)
        );

        IssueAddSubIssueFailedException exception = await Assert.ThrowsAsync<IssueAddSubIssueFailedException>(() =>
            this.AddSubIssueAsync(upstream)
        );

        Assert.Equal(expected: FAILED_MESSAGE, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }
}
