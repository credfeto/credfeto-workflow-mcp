using System;
using System.Globalization;
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

public sealed class PrReadyAtServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PR = 73;
    private const string CREATED_AT = "2026-01-01T00:00:00Z";

    private const string PULL = "{\"number\":73,\"created_at\":\"" + CREATED_AT + "\"}";

    private static readonly Uri PullUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/73");

    private static readonly Uri TimelineUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/73/timeline?per_page=100"
    );

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    private static string Event(string name, string createdAt)
    {
        return "{\"event\":\"" + name + "\",\"created_at\":\"" + createdAt + "\"}";
    }

    private Task<PrReadyAtResponse> ReadAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PR
    )
    {
        PrReadyAtService service = new(
            new GitHubRestClient(
                httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
                logger: this.GetTypedLogger<GitHubRestClient>()
            )
        );

        return service
            .ReadAsync(repository: repository, pullRequest: pullRequest, cancellationToken: this.CancellationToken())
            .AsTask();
    }

    [Fact]
    public async Task APullRequestNeverMarkedReadyIsReadyFromWhenItWasCreatedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () => Responses.Json("[" + Event(name: "labeled", createdAt: "2026-01-02T00:00:00Z") + "]")
        );

        PrReadyAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: new(ReadyAt: CREATED_AT, Source: PrReadyAtService.SOURCE_CREATED), actual: response);
        Assert.Equal(expected: PullUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: TimelineUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task TheLastReadyEventOnAnyPageIsTakenAsync()
    {
        string secondPage = TimelineUri + "&page=2";

        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () =>
                Responses.Json(
                    json: "["
                        + Event(name: "ready_for_review", createdAt: "2026-01-02T00:00:00Z")
                        + ","
                        + Event(name: "convert_to_draft", createdAt: "2026-01-03T00:00:00Z")
                        + "]",
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
            () =>
                Responses.Json(
                    "["
                        + Event(name: "ready_for_review", createdAt: "2026-01-04T00:00:00Z")
                        + ","
                        + Event(name: "commented", createdAt: "2026-01-05T00:00:00Z")
                        + "]"
                )
        );

        PrReadyAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: new(ReadyAt: "2026-01-04T00:00:00Z", Source: PrReadyAtService.SOURCE_READY_FOR_REVIEW),
            actual: response
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: "pull request " + REPOSITORY + "#73 was not found", actual: exception.Message);
    }

    [Theory]
    [InlineData("{\"number\":74,\"created_at\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"number\":73}")]
    [InlineData("{\"number\":73,\"created_at\":\"yesterday\"}")]
    public async Task AnUnusablePullRequestFailsAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(json));

        PrReadyAtFailedException exception = await Assert.ThrowsAsync<PrReadyAtFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read when pull request " + REPOSITORY + "#73 became ready for review",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task AnUnreadableReadyTimeFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () => Responses.Json("[{\"event\":\"ready_for_review\"}]")
        );

        await Assert.ThrowsAsync<PrReadyAtFailedException>(() => this.ReadAsync(upstream));
    }

    [Fact]
    public async Task ATimelineFailureFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () => Responses.Status(HttpStatusCode.InternalServerError)
        );

        await Assert.ThrowsAsync<PrReadyAtFailedException>(() => this.ReadAsync(upstream));
    }

    [Theory]
    [InlineData("not-a-repository", PR)]
    [InlineData(REPOSITORY, 0)]
    [InlineData(REPOSITORY, -1)]
    public async Task InvalidInputIsRefusedWithoutAnyRequestAsync(string repository, int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, repository: repository, pullRequest: pullRequest)
        );

        Assert.Empty(upstream.Requests);
    }
}
