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

public sealed class PrAutoMergeAtServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PR = 62;

    private const string PULL = "{\"number\":62,\"created_at\":\"2026-01-01T00:00:00Z\"}";

    private static readonly Uri PullUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/62");

    private static readonly Uri TimelineUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/62/timeline?per_page=100"
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

    private Task<PrAutoMergeAtResponse> ReadAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PR
    )
    {
        PrAutoMergeAtService service = new(
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
    public async Task TheLatestEnabledEventIsTakenAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () =>
                Responses.Json(
                    "["
                        + Event(name: "auto_merge_enabled", createdAt: "2026-01-02T00:00:00Z")
                        + ","
                        + Event(name: "auto_merge_disabled", createdAt: "2026-01-03T00:00:00Z")
                        + ","
                        + Event(name: "auto_merge_enabled", createdAt: "2026-01-04T00:00:00Z")
                        + ","
                        + Event(name: "labeled", createdAt: "2026-01-05T00:00:00Z")
                        + "]"
                )
        );

        PrAutoMergeAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: new(Pr: PR, EnabledAt: "2026-01-04T00:00:00Z"), actual: response);
        Assert.Equal(expected: PullUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: TimelineUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task APullRequestNeverEnabledForAutoMergeIsReportedAsNeverEnabledAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () => Responses.Json("[" + Event(name: "labeled", createdAt: "2026-01-02T00:00:00Z") + "]")
        );

        PrAutoMergeAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: new(Pr: PR, EnabledAt: PrAutoMergeAtService.NEVER_ENABLED), actual: response);
    }

    [Fact]
    public async Task AnEmptyTimelineIsReportedAsNeverEnabledAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(PULL), () => Responses.Json("[]"));

        PrAutoMergeAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: new(Pr: PR, EnabledAt: PrAutoMergeAtService.NEVER_ENABLED), actual: response);
    }

    [Fact]
    public async Task TheLatestEnabledEventOnAnyPageIsTakenAsync()
    {
        string secondPage = TimelineUri + "&page=2";

        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () =>
                Responses.Json(
                    json: "[" + Event(name: "auto_merge_enabled", createdAt: "2026-01-02T00:00:00Z") + "]",
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
            () =>
                Responses.Json(
                    "["
                        + Event(name: "auto_merge_enabled", createdAt: "2026-01-04T00:00:00Z")
                        + ","
                        + Event(name: "commented", createdAt: "2026-01-05T00:00:00Z")
                        + "]"
                )
        );

        PrAutoMergeAtResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: new(Pr: PR, EnabledAt: "2026-01-04T00:00:00Z"), actual: response);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        Assert.Equal(expected: new Uri(secondPage), actual: upstream.Requests[2].RequestUri);
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: "pull request " + REPOSITORY + "#62 was not found", actual: exception.Message);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AnotherPullRequestReturnedFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() =>
            Responses.Json("{\"number\":63,\"created_at\":\"2026-01-01T00:00:00Z\"}")
        );

        PrAutoMergeAtFailedException exception = await Assert.ThrowsAsync<PrAutoMergeAtFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read when auto-merge was enabled on pull request " + REPOSITORY + "#62",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData("[{\"event\":\"auto_merge_enabled\"}]")]
    [InlineData("[{\"event\":\"auto_merge_enabled\",\"created_at\":\"yesterday\"}]")]
    [InlineData("{\"event\":\"auto_merge_enabled\"}")]
    public async Task AnUnreadableTimelineFailsAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(PULL), () => Responses.Json(json));

        await Assert.ThrowsAsync<PrAutoMergeAtFailedException>(() => this.ReadAsync(upstream));
    }

    [Fact]
    public async Task ATimelineFailureFailsAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            () => Responses.Json(PULL),
            () => Responses.Status(HttpStatusCode.InternalServerError)
        );

        PrAutoMergeAtFailedException exception = await Assert.ThrowsAsync<PrAutoMergeAtFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.IsType<HttpRequestException>(exception.InnerException);
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
