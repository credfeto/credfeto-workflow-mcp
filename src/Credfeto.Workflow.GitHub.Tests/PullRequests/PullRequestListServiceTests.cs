using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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

public sealed class PullRequestListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FORK = "alice/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not list the pull requests of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY = "credfeto/credfeto-workflow-mcp has more than 1000 open pull requests to list";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls?state=open&per_page=100"
    );

    private static string Item(
        int number,
        string head = "feat/1-thing",
        string labels = "",
        bool draft = false,
        string user = """{"login":"credfeto","type":"User"}""",
        string headRepository = "\"" + REPOSITORY + "\"",
        string title = "A change"
    )
    {
        string text = number.ToString(CultureInfo.InvariantCulture);
        string labelJson = string.Join(
            separator: ',',
            values: labels
                .Split(separator: ',', options: StringSplitOptions.RemoveEmptyEntries)
                .Select(label => "{\"name\":\"" + label + "\"}")
        );
        string headRepositoryJson = StringComparer.Ordinal.Equals(x: headRepository, y: "null")
            ? "null"
            : "{\"full_name\":" + headRepository + "}";

        return "{\"number\":"
            + text
            + ",\"title\":\""
            + title
            + "\",\"html_url\":\"https://github.com/"
            + REPOSITORY
            + "/pull/"
            + text
            + "\",\"draft\":"
            + (draft ? "true" : "false")
            + ",\"user\":"
            + user
            + ",\"head\":{\"ref\":\""
            + head
            + "\",\"repo\":"
            + headRepositoryJson
            + "},\"base\":{\"ref\":\"main\",\"repo\":{\"full_name\":\""
            + REPOSITORY
            + "\"}},\"labels\":["
            + labelJson
            + "]}";
    }

    private static string Bare(string extra)
    {
        return Page("{\"number\":1,\"html_url\":\"https://github.com/" + REPOSITORY + "/pull/1\"" + extra + "}");
    }

    private static string Page(params string[] items)
    {
        return "[" + string.Join(separator: ',', value: items) + "]";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private PullRequestListService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<PullRequestListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        int? issue = null,
        string? head = null,
        IReadOnlyList<string>? labels = null,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(
                repository: repository,
                issue: issue,
                head: head,
                labels: labels,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<PullRequestListResponse> ListJsonAsync(
        string json,
        int? issue = null,
        string? head = null,
        IReadOnlyList<string>? labels = null
    )
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream: upstream, issue: issue, head: head, labels: labels);
    }

    private static IReadOnlyList<int> Numbers(PullRequestListResponse response)
    {
        return [.. response.PullRequests.Select(entry => entry.Number)];
    }

    [Fact]
    public async Task ListsEveryOpenPullRequestInNumberOrderAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item(9), Item(3), Item(5))));

        PullRequestListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: [3, 5, 9], actual: Numbers(response));
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 7, head: "feat/7-list", title: "Add list"))
        );

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: 7, actual: entry.Number);
        Assert.Equal(expected: "feat/7-list", actual: entry.Head);
        Assert.Equal(expected: "main", actual: entry.Base);
        Assert.Equal(expected: "credfeto", actual: entry.Author);
        Assert.Equal(expected: "ready", actual: entry.State);
        Assert.Empty(entry.Flags);
        Assert.Equal(expected: new Uri("https://github.com/" + REPOSITORY + "/pull/7"), actual: entry.Url);
        Assert.Equal(expected: "Add list", actual: entry.Title);
    }

    [Fact]
    public async Task DraftIsReportedAsDraftAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(Page(Item(number: 1, draft: true)));

        Assert.Equal(expected: "draft", actual: Assert.Single(response.PullRequests).State);
    }

    [Fact]
    public async Task EmptyFieldsAreShownAsPlaceholderAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(Page(Item(number: 1, title: "  ", user: "null")));

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: "-", actual: entry.Title);
        Assert.Equal(expected: "-", actual: entry.Author);
    }

    [Fact]
    public async Task OneLabelKeepsOnlyCarriersAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(Item(number: 1, labels: "bug"), Item(number: 2, labels: "feature"), Item(number: 3)),
            labels: ["bug"]
        );

        Assert.Equal(expected: [1], actual: Numbers(response));
    }

    [Fact]
    public async Task SeveralLabelsMustAllBePresentAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(
                Item(number: 1, labels: "bug,mcp,extra"),
                Item(number: 2, labels: "bug"),
                Item(number: 3, labels: "mcp")
            ),
            labels: ["bug", "mcp"]
        );

        Assert.Equal(expected: [1], actual: Numbers(response));
    }

    [Fact]
    public async Task LabelsAreComparedCaseInsensitivelyAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(Item(number: 4, labels: "Needs Review")),
            labels: ["needs review"]
        );

        Assert.Equal(expected: [4], actual: Numbers(response));
    }

    [Fact]
    public async Task NoMatchIsEmptyAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(Item(number: 1, labels: "bug")),
            labels: ["feature"]
        );

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.PullRequests);
    }

    [Fact]
    public async Task HeadKeepsOnlyExactBranchAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(
                Item(number: 1, head: "feat/1-thing"),
                Item(number: 2, head: "feat/1-thing-more"),
                Item(number: 3, head: "Feat/1-Thing")
            ),
            head: "feat/1-thing"
        );

        Assert.Equal(expected: [1], actual: Numbers(response));
    }

    [Theory]
    [InlineData("feat/12-x")]
    [InlineData("fix/012")]
    [InlineData("12")]
    [InlineData("release/1.12")]
    public async Task HeadNamingIssueIsFlaggedAsync(string head)
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Page(Item(number: 1, head: head)), issue: 12);

        Assert.Equal(expected: ["issue"], actual: Assert.Single(response.PullRequests).Flags);
    }

    [Theory]
    [InlineData("feat/123-x")]
    [InlineData("feat/112")]
    [InlineData("feat/x")]
    public async Task HeadNotNamingIssueIsNotFlaggedAsync(string head)
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Page(Item(number: 1, head: head)), issue: 12);

        Assert.Empty(Assert.Single(response.PullRequests).Flags);
    }

    [Fact]
    public async Task WithoutIssueNothingIsIssueFlaggedAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(Page(Item(number: 1, head: "feat/12-x")));

        Assert.Empty(Assert.Single(response.PullRequests).Flags);
    }

    [Fact]
    public async Task GitHubActionsAuthorIsFlaggedAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 1, user: """{"login":"github-actions[bot]","type":"Bot"}"""))
        );

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: "app/github-actions", actual: entry.Author);
        Assert.Equal(expected: ["github-actions"], actual: entry.Flags);
    }

    [Fact]
    public async Task OtherBotAuthorIsAppAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 1, user: """{"login":"dependabot[bot]","type":"Bot"}"""))
        );

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: "app/dependabot", actual: entry.Author);
        Assert.Empty(entry.Flags);
    }

    [Fact]
    public async Task OtherAuthorTypeIsPrefixedAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 1, user: """{"login":"acme","type":"Organization"}"""))
        );

        Assert.Equal(expected: "organization/acme", actual: Assert.Single(response.PullRequests).Author);
    }

    [Fact]
    public async Task ForkIsFlaggedAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 1, headRepository: "\"" + FORK + "\""))
        );

        Assert.Equal(expected: ["fork"], actual: Assert.Single(response.PullRequests).Flags);
    }

    [Fact]
    public async Task DeletedHeadRepositoryIsForkAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(Page(Item(number: 1, headRepository: "null")));

        Assert.Equal(expected: ["fork"], actual: Assert.Single(response.PullRequests).Flags);
    }

    [Theory]
    [InlineData(
        """{"ref":"feat/1-thing"}""",
        """{"ref":"main","repo":{"full_name":"credfeto/credfeto-workflow-mcp"}}"""
    )]
    [InlineData(
        """{"ref":"feat/1-thing","repo":{"full_name":"credfeto/credfeto-workflow-mcp"}}""",
        """{"ref":"main"}"""
    )]
    [InlineData("""{"ref":"feat/1-thing","repo":{"full_name":"credfeto/credfeto-workflow-mcp"}}""", "null")]
    public async Task MissingRepositoryOnEitherSideIsForkAsync(string headJson, string baseJson)
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Bare(", \"head\":" + headJson + ",\"base\":" + baseJson)
        );

        Assert.Equal(expected: ["fork"], actual: Assert.Single(response.PullRequests).Flags);
    }

    [Fact]
    public async Task MissingHeadAndBaseAreShownAsPlaceholderAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Bare(string.Empty), issue: 12);

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: "-", actual: entry.Head);
        Assert.Equal(expected: "-", actual: entry.Base);
        Assert.Equal(expected: "-", actual: entry.Author);
        Assert.Equal(expected: ["fork"], actual: entry.Flags);
    }

    [Fact]
    public async Task EmptyHeadRefIsNotIssueFlaggedAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Page(Item(number: 1, head: "")), issue: 12);

        PullRequestListEntry entry = Assert.Single(response.PullRequests);
        Assert.Equal(expected: "-", actual: entry.Head);
        Assert.Empty(entry.Flags);
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "head":null""")]
    [InlineData(""", "head":{}""")]
    public async Task MissingHeadRefIsNotOnHeadBranchAsync(string extra)
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Bare(extra), head: "feat/1-thing");

        Assert.Empty(response.PullRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(""", "labels":null""")]
    public async Task MissingLabelsHaveNoLabelAsync(string extra)
    {
        PullRequestListResponse response = await this.ListJsonAsync(json: Bare(extra), labels: ["bug"]);

        Assert.Empty(response.PullRequests);
    }

    [Fact]
    public async Task MissingAuthorTypeIsPlainLoginAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            Page(Item(number: 1, user: """{"login":"credfeto"}"""))
        );

        Assert.Equal(expected: "credfeto", actual: Assert.Single(response.PullRequests).Author);
    }

    [Fact]
    public async Task FlagsAreInFixedOrderAsync()
    {
        PullRequestListResponse response = await this.ListJsonAsync(
            json: Page(
                Item(
                    number: 1,
                    head: "feat/5-x",
                    user: """{"login":"github-actions[bot]","type":"Bot"}""",
                    headRepository: "\"" + FORK + "\""
                )
            ),
            issue: 5
        );

        Assert.Equal(expected: ["issue", "github-actions", "fork"], actual: Assert.Single(response.PullRequests).Flags);
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/pulls?state=open&per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(json: Page(Item(4)), nextLink: "<" + secondPage + ">; rel=\"next\""),
                1 => Responses.Json(Page(Item(2))),
                _ => Unexpected(index),
            }
        );

        PullRequestListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: [2, 4], actual: Numbers(response));
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        string json = Page([.. Enumerable.Range(start: 1, count: 1001).Select(number => Item(number))]);

        PullRequestListFailedException exception = await Assert.ThrowsAsync<PullRequestListFailedException>(() =>
            this.ListJsonAsync(json)
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
    }

    [Fact]
    public async Task CapCountsOnlyTheHeadBranchAsync()
    {
        string json = Page([
            .. Enumerable
                .Range(start: 1, count: 1001)
                .Select(number => Item(number: number, head: number == 1 ? "feat/wanted" : "feat/other")),
        ]);

        PullRequestListResponse response = await this.ListJsonAsync(json: json, head: "feat/wanted");

        Assert.Equal(expected: [1], actual: Numbers(response));
    }

    [Fact]
    public async Task CapCountsBeforeLabelsAsync()
    {
        string json = Page([.. Enumerable.Range(start: 1, count: 1001).Select(number => Item(number))]);

        PullRequestListFailedException exception = await Assert.ThrowsAsync<PullRequestListFailedException>(() =>
            this.ListJsonAsync(json: json, labels: ["absent"])
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        PullRequestListFailedException exception = await Assert.ThrowsAsync<PullRequestListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("[null]")]
    [InlineData("""[{"title":"no number","html_url":"https://github.com/o/r/pull/1"}]""")]
    [InlineData("""[{"number":1,"html_url":"not a link"}]""")]
    [InlineData("""[{"number":1}]""")]
    [InlineData("""[{"number":1,"html_url":"ftp://github.com/o/r/pull/1"}]""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        PullRequestListFailedException exception = await Assert.ThrowsAsync<PullRequestListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("""[{"number":1,"html_url":"https://github.com/o/r/pull/1","labels":[null]}]""")]
    [InlineData("""[{"number":1,"html_url":"https://github.com/o/r/pull/1","labels":[{}]}]""")]
    public async Task InvalidLabelsResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        PullRequestListFailedException exception = await Assert.ThrowsAsync<PullRequestListFailedException>(() =>
            this.ListAsync(upstream: upstream, labels: ["bug"])
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    [InlineData("credfeto/bad name")]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, repository: repository)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidIssueIsRejectedWithoutRequestAsync(int issue)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, issue: issue)
        );

        Assert.Contains(
            expectedSubstring: "issue",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad branch")]
    [InlineData("feat/..")]
    public async Task InvalidHeadIsRejectedWithoutRequestAsync(string head)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, head: head)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001label")]
    [InlineData("bad\"label")]
    [InlineData("bad\\label")]
    public async Task InvalidLabelIsRejectedWithoutRequestAsync(string label)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, labels: ["bug", label])
        );

        Assert.Contains(
            expectedSubstring: "labels",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task OverlongLabelIsRejectedWithoutRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, labels: [new string(c: 'a', count: 51)])
        );

        Assert.Empty(upstream.Requests);
    }
}
