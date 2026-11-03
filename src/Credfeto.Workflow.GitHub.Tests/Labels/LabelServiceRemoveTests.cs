using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Labels;

public sealed class LabelServiceRemoveTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string BLOCKED_RULE = "Blocked may only be removed when it was last added by this agent";
    private const string AGENT = """{"login":"workflow-agent"}""";

    private const string ISSUE_URI = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/5";

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private Task<LabelChangeResponse> RemoveAsync(
        RecordingHttpMessageHandler upstream,
        IReadOnlyList<string> labels,
        int? pullRequest = 5,
        int? issue = null
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        LabelService service = new(rest);

        return service
            .RemoveAsync(
                repository: REPOSITORY,
                pullRequest: pullRequest,
                issue: issue,
                labels: labels,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, string uri)
    {
        Assert.Equal(expected: method, actual: request.Method);
        Assert.Equal(expected: new Uri(uri), actual: request.RequestUri);
    }

    private static string Timeline(params (string Event, string Actor)[] events)
    {
        List<string> items = [];

        foreach ((string timelineEvent, string actor) in events)
        {
            items.Add(
                "{\"event\":\""
                    + timelineEvent
                    + "\",\"actor\":{\"login\":\""
                    + actor
                    + "\"},\"label\":{\"name\":\"Blocked\"}}"
            );
        }

        return "[" + string.Join(separator: ',', values: items) + "]";
    }

    [Fact]
    public async Task RemovesEachPresentLabelAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Bug"},{"name":"Tech Debt"},{"name":"Enhancement"}]"""),
                1 => Responses.Json("""[{"name":"Tech Debt"},{"name":"Enhancement"}]"""),
                2 => Responses.Json("""[{"name":"Enhancement"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.RemoveAsync(upstream: upstream, labels: ["bug", "Tech Debt"]);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: 5, actual: response.Number);
        Assert.Equal(expected: ["Enhancement"], actual: response.Labels);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI + "/labels?per_page=100"),
            request => AssertRequest(request: request, method: HttpMethod.Delete, uri: ISSUE_URI + "/labels/Bug"),
            request =>
                AssertRequest(request: request, method: HttpMethod.Delete, uri: ISSUE_URI + "/labels/Tech%20Debt")
        );
    }

    [Fact]
    public async Task AbsentLabelIsNotRemovedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Enhancement"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.RemoveAsync(
            upstream: upstream,
            labels: ["Bug"],
            pullRequest: null,
            issue: 5
        );

        Assert.Equal(expected: ["Enhancement"], actual: response.Labels);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task OnHoldIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Bug", "on hold"])
        );

        Assert.Equal(expected: "On Hold", actual: exception.Label);
        Assert.Equal(expected: "On Hold may only be removed by a human", actual: exception.Rule);
        Assert.Equal(
            expected: "the label On Hold was refused: On Hold may only be removed by a human",
            actual: exception.Message
        );
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task PriorityLabelIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Urgent"])
        );

        Assert.Equal(expected: "a priority label may only be changed by a human", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task BlockedAddedByTheAgentIsRemovedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Blocked"},{"name":"Bug"}]"""),
                1 => Responses.Json(AGENT),
                2 => Responses.Json(
                    Timeline(("labeled", "a-human"), ("unlabeled", "a-human"), ("labeled", "Workflow-Agent"))
                ),
                3 => Responses.Json("""[{"name":"Bug"}]"""),
                _ => Unexpected(index),
            }
        );

        LabelChangeResponse response = await this.RemoveAsync(upstream: upstream, labels: ["Blocked"]);

        Assert.Equal(expected: ["Bug"], actual: response.Labels);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI + "/labels?per_page=100"),
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: GitHubServices.ApiBaseUrl + "user"),
            request =>
                AssertRequest(request: request, method: HttpMethod.Get, uri: ISSUE_URI + "/timeline?per_page=100"),
            request => AssertRequest(request: request, method: HttpMethod.Delete, uri: ISSUE_URI + "/labels/Blocked")
        );
    }

    [Fact]
    public async Task BlockedAddedByAnotherUserIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Blocked"}]"""),
                1 => Responses.Json(AGENT),
                2 => Responses.Json(Timeline(("labeled", "a-human"))),
                _ => Unexpected(index),
            }
        );

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Blocked", "Bug"])
        );

        Assert.Equal(expected: "Blocked", actual: exception.Label);
        Assert.Equal(expected: BLOCKED_RULE, actual: exception.Rule);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task BlockedReAddedByAHumanIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Blocked"}]"""),
                1 => Responses.Json(AGENT),
                2 => Responses.Json(
                    Timeline(("labeled", "workflow-agent"), ("unlabeled", "a-human"), ("labeled", "a-human"))
                ),
                _ => Unexpected(index),
            }
        );

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Blocked"])
        );

        Assert.Equal(expected: BLOCKED_RULE, actual: exception.Rule);
    }

    [Fact]
    public async Task BlockedWithNoLabeledEventIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Blocked"}]"""),
                1 => Responses.Json(AGENT),
                2 => Responses.Json("[]"),
                _ => Unexpected(index),
            }
        );

        LabelRefusedException exception = await Assert.ThrowsAsync<LabelRefusedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Blocked"])
        );

        Assert.Equal(expected: BLOCKED_RULE, actual: exception.Rule);
    }

    [Fact]
    public async Task AgentWithNoLoginFailsTheChangeAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Blocked"}]"""),
                1 => Responses.Json("{}"),
                _ => Unexpected(index),
            }
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Blocked"])
        );

        Assert.Equal(
            expected: "could not change the labels of credfeto/credfeto-workflow-mcp#5",
            actual: exception.Message
        );
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Bug"])
        );

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task MissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        _ = await Assert.ThrowsAsync<IssueNotFoundException>(() =>
            this.RemoveAsync(upstream: upstream, labels: ["Bug"], pullRequest: null, issue: 5)
        );
    }
}
