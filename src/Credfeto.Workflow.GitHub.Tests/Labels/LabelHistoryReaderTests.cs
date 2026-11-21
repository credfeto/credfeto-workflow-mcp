using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Labels;

public sealed class LabelHistoryReaderTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string TIME = "2026-01-02T03:04:05Z";
    private const string ISSUE_JSON = """{"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/7"}""";
    private const string PR_JSON =
        """{"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/7","pull_request":{"url":"x"}}""";

    private static readonly Uri ItemUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/7");

    private static readonly Uri TimelineUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/7/timeline?per_page=100"
    );

    private static string Event(string kind, string? label, string? actor, string time = TIME)
    {
        string who = actor is null ? "null" : "{\"login\":\"" + actor + "\"}";
        string what = label is null ? "null" : "{\"name\":\"" + label + "\"}";

        return "{\"event\":\""
            + kind
            + "\",\"created_at\":\""
            + time
            + "\",\"actor\":"
            + who
            + ",\"label\":"
            + what
            + "}";
    }

    private static string Page(params string[] events)
    {
        return "[" + string.Join(separator: ',', values: events) + "]";
    }

    private static HttpResponseMessage Serve(int index, string item, string timeline)
    {
        return index switch
        {
            0 => Responses.Json(item),
            1 => Responses.Json(timeline),
            _ => throw new InvalidOperationException(
                "Unexpected request " + index.ToString(CultureInfo.InvariantCulture)
            ),
        };
    }

    private static string Numbered(int count)
    {
        return Page([
            .. Enumerable
                .Range(start: 1, count: count)
                .Select(number =>
                    Event(kind: "labeled", label: "label-" + number.ToString(CultureInfo.InvariantCulture), actor: "a")
                ),
        ]);
    }

    private async Task<LabelHistoryResult> ReadAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int? issue = 7,
        int? pr = null,
        string? label = null
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return await new LabelHistoryReader(rest).ReadAsync(
            repository: repository,
            issue: issue,
            pr: pr,
            label: label,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task<LabelHistoryResult> ReadTimelineAsync(string timeline, string? label = null)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Serve(index: index, item: ISSUE_JSON, timeline: timeline)
        );

        return await this.ReadAsync(upstream: upstream, label: label);
    }

    [Fact]
    public async Task ReadsTheChangesOldestFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Serve(
                index: index,
                item: ISSUE_JSON,
                timeline: Page(
                    Event(kind: "labeled", label: "bug", actor: "credfeto", time: "2026-01-01T00:00:00Z"),
                    "{\"event\":\"commented\"}",
                    Event(kind: "unlabeled", label: "bug", actor: "steward[bot]", time: "2026-01-02T00:00:00Z")
                )
            )
        );

        LabelHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(expected: 7, actual: result.Number);
        Assert.Equal(
            expected:
            [
                new LabelHistoryEntry(Action: "added", Time: "2026-01-01T00:00:00Z", Actor: "credfeto", Label: "bug"),
                new LabelHistoryEntry(
                    Action: "removed",
                    Time: "2026-01-02T00:00:00Z",
                    Actor: "steward[bot]",
                    Label: "bug"
                ),
            ],
            actual: result.Changes
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: ItemUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: TimelineUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task APullRequestIsReadWhenAskedForAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Serve(index: index, item: PR_JSON, timeline: Page(Event(kind: "labeled", label: "bug", actor: "a")))
        );

        LabelHistoryResult result = await this.ReadAsync(upstream: upstream, issue: null, pr: 7);

        _ = Assert.Single(result.Changes);
    }

    [Fact]
    public async Task NoChangesIsAnEmptyListAsync()
    {
        LabelHistoryResult result = await this.ReadTimelineAsync(Page("{\"event\":\"closed\"}"));

        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task ActorAndLabelAreSanitisedAsync()
    {
        LabelHistoryResult result = await this.ReadTimelineAsync(
            Page(
                Event(kind: "labeled", label: "a\\u0001b", actor: null),
                Event(kind: "labeled", label: null, actor: "ghost"),
                "{\"event\":\"unlabeled\",\"created_at\":\""
                    + TIME
                    + "\",\"actor\":{\"login\":\"\"},\"label\":{\"name\":\"\"}}"
            )
        );

        Assert.Equal(
            expected: ["-", "ghost", "-"],
            actual: result.Changes.Select(static c => c.Actor),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(
            expected: ["a b", "-", "-"],
            actual: result.Changes.Select(static c => c.Label),
            comparer: StringComparer.Ordinal
        );
    }

    [Theory]
    [InlineData("bug", 2)]
    [InlineData("BUG", 2)]
    [InlineData("Bug", 2)]
    [InlineData("bugs", 0)]
    [InlineData("é", 1)]
    [InlineData("É", 0)]
    [InlineData("🐛", 1)]
    [InlineData("a,b", 1)]
    [InlineData("-", 0)]
    public async Task FilterMatchesOnlyAsciiCaseInsensitivelyAsync(string filter, int expected)
    {
        LabelHistoryResult result = await this.ReadTimelineAsync(
            Page(
                Event(kind: "labeled", label: "bug", actor: "a"),
                Event(kind: "unlabeled", label: "Bug", actor: "a"),
                Event(kind: "labeled", label: "é", actor: "a"),
                Event(kind: "labeled", label: "🐛", actor: "a"),
                Event(kind: "labeled", label: "a,b", actor: "a"),
                Event(kind: "labeled", label: null, actor: "a")
            ),
            label: filter
        );

        Assert.Equal(expected: expected, actual: result.Changes.Count);
    }

    [Fact]
    public async Task ExactlyTheCapIsReadAsync()
    {
        LabelHistoryResult result = await this.ReadTimelineAsync(Numbered(1000));

        Assert.Equal(expected: 1000, actual: result.Changes.Count);
    }

    [Fact]
    public async Task OverTheCapIsRefusedEvenWhenTheFilterWouldKeepOneAsync()
    {
        LabelHistoryFailedException exception = await Assert.ThrowsAsync<LabelHistoryFailedException>(() =>
            this.ReadTimelineAsync(timeline: Numbered(1001), label: "label-1")
        );

        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#7 has more than 1000 label changes to read",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task OtherTimelineEventsDoNotCountTowardsTheCapAsync()
    {
        string timeline =
            "[" + string.Join(',', Enumerable.Range(0, 1200).Select(static _ => "{\"event\":\"commented\"}")) + "]";

        LabelHistoryResult result = await this.ReadTimelineAsync(timeline);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/issues/7/timeline?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(ISSUE_JSON),
                1 => Responses.Json(
                    json: Page(Event(kind: "labeled", label: "first", actor: "a")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
                2 => Responses.Json(Page(Event(kind: "unlabeled", label: "second", actor: "a"))),
                _ => throw new InvalidOperationException("Unexpected request"),
            }
        );

        LabelHistoryResult result = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: ["first", "second"],
            actual: result.Changes.Select(static c => c.Label),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public async Task MissingItemIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        IssueNotFoundException exception = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "the issue credfeto/credfeto-workflow-mcp#7 was not found, or the caller cannot see it",
            actual: exception.Message
        );
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream: upstream, issue: null, pr: 7)
        );

        Assert.Equal(
            expected: "the pull request credfeto/credfeto-workflow-mcp#7 was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task APullRequestIsNotAnIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index => Serve(index: index, item: PR_JSON, timeline: "[]"));

        _ = await Assert.ThrowsAsync<IssueNotFoundException>(() => this.ReadAsync(upstream));
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AnIssueIsNotAPullRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Serve(index: index, item: ISSUE_JSON, timeline: "[]")
        );

        _ = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream: upstream, issue: null, pr: 7)
        );
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task NotFoundOnTheTimelineIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(ISSUE_JSON) : Responses.Status(HttpStatusCode.NotFound)
        );

        _ = await Assert.ThrowsAsync<IssueNotFoundException>(() => this.ReadAsync(upstream));
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        LabelHistoryFailedException exception = await Assert.ThrowsAsync<LabelHistoryFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read the label history of credfeto/credfeto-workflow-mcp#7",
            actual: exception.Message
        );
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("""[{"event":"labeled","actor":null,"label":{"name":"bug"}}]""")]
    [InlineData("""[{"event":"labeled","created_at":"yesterday","label":{"name":"bug"}}]""")]
    [InlineData("""[{"event":"labeled","created_at":"2026-01-02T03:04:05+01:00","label":{"name":"bug"}}]""")]
    [InlineData("{}")]
    public async Task InvalidTimelineIsFailedAsync(string timeline)
    {
        LabelHistoryFailedException exception = await Assert.ThrowsAsync<LabelHistoryFailedException>(() =>
            this.ReadTimelineAsync(timeline)
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("", 7, null, null, "repository")]
    [InlineData(REPOSITORY, null, null, null, "pr")]
    [InlineData(REPOSITORY, 7, 8, null, "pr")]
    [InlineData(REPOSITORY, 0, null, null, "issue")]
    [InlineData(REPOSITORY, -1, null, null, "issue")]
    [InlineData(REPOSITORY, null, 0, null, "pr")]
    [InlineData(REPOSITORY, 7, null, "", "label")]
    [InlineData(REPOSITORY, 7, null, "  ", "label")]
    [InlineData(REPOSITORY, 7, null, "a\nb", "label")]
    [InlineData(REPOSITORY, 7, null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "label")]
    public async Task InvalidInputIsRejectedWithoutRequestAsync(
        string repository,
        int? issue,
        int? pr,
        string? label,
        string parameter
    )
    {
        using RecordingHttpMessageHandler upstream = new(static _ => throw new InvalidOperationException("no request"));

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, repository: repository, issue: issue, pr: pr, label: label)
        );

        Assert.Contains(
            expectedSubstring: parameter,
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }
}
