using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Advisories;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Advisories;

public sealed class SecurityAdvisoriesServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string GHSA_ID = "GHSA-5crp-9r3c-p9vr";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not list the security advisories of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY =
        "credfeto/credfeto-workflow-mcp has more than 1000 security advisories, so none is listed";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/security-advisories?per_page=100"
    );

    private static string Advisory(
        string ghsaId = GHSA_ID,
        string severity = "high",
        string state = "published",
        string summary = "A summary"
    )
    {
        return "{\"ghsa_id\":\""
            + ghsaId
            + "\",\"severity\":\""
            + severity
            + "\",\"state\":\""
            + state
            + "\",\"summary\":\""
            + summary
            + "\"}";
    }

    private static string Page(params string[] advisories)
    {
        return "[" + string.Join(separator: ',', values: advisories) + "]";
    }

    private static string Repeated(int count)
    {
        return Page([.. Enumerable.Repeat(element: Advisory(), count: count)]);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private SecurityAdvisoriesService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<SecurityAdvisoriesResult> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<SecurityAdvisoriesResult> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    [Fact]
    public async Task ReadsTheAdvisoriesOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Advisory())));

        SecurityAdvisoriesResult result = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(
            expected:
            [
                new SecurityAdvisoryEntry(GhsaId: GHSA_ID, Severity: "high", State: "published", Summary: "A summary"),
            ],
            actual: result.Advisories
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task NoAdvisoriesIsEmptyAsync()
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Empty(result.Advisories);
    }

    [Fact]
    public async Task AdvisoriesKeepGitHubsOrderAsync()
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(
            Page(
                Advisory(ghsaId: "GHSA-xxxx-xxxx-xxxx"),
                Advisory(ghsaId: "GHSA-2222-2222-2222"),
                Advisory(ghsaId: "GHSA-mmmm-mmmm-mmmm")
            )
        );

        Assert.Equal(
            expected: ["GHSA-xxxx-xxxx-xxxx", "GHSA-2222-2222-2222", "GHSA-mmmm-mmmm-mmmm"],
            actual: result.Advisories.Select(advisory => advisory.GhsaId),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/security-advisories?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(
                    json: Page(Advisory(ghsaId: "GHSA-cccc-cccc-cccc")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
                1 => Responses.Json(Page(Advisory(ghsaId: "GHSA-ffff-ffff-ffff"))),
                _ => Unexpected(index),
            }
        );

        SecurityAdvisoriesResult result = await this.ListAsync(upstream);

        Assert.Equal(
            expected: ["GHSA-cccc-cccc-cccc", "GHSA-ffff-ffff-ffff"],
            actual: result.Advisories.Select(advisory => advisory.GhsaId),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    public async Task EveryKnownSeverityIsListedAsync(string severity)
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(Page(Advisory(severity: severity)));

        Assert.Equal(expected: severity, actual: Assert.Single(result.Advisories).Severity);
    }

    [Theory]
    [InlineData("published")]
    [InlineData("closed")]
    [InlineData("withdrawn")]
    [InlineData("draft")]
    [InlineData("triage")]
    public async Task EveryKnownStateIsListedAsync(string state)
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(Page(Advisory(state: state)));

        Assert.Equal(expected: state, actual: Assert.Single(result.Advisories).State);
    }

    [Theory]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","state":"draft","summary":"s"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":null,"state":"draft","summary":"s"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"","state":"draft","summary":"s"}]""")]
    public async Task AMissingSeverityIsADashAsync(string json)
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(json);

        Assert.Equal(expected: "-", actual: Assert.Single(result.Advisories).Severity);
    }

    [Theory]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"low","state":"draft"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"low","state":"draft","summary":null}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"low","state":"draft","summary":""}]""")]
    public async Task AMissingSummaryIsADashAsync(string json)
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(json);

        Assert.Equal(expected: "-", actual: Assert.Single(result.Advisories).Summary);
    }

    [Fact]
    public async Task ControlCharactersInTheSummaryAreSpacesAsync()
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(
            Page(Advisory(summary: "a\\nsummary\\u0007x\\u001by\\u2028z"))
        );

        Assert.Equal(expected: "a summary x y z", actual: Assert.Single(result.Advisories).Summary);
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        SecurityAdvisoriesFailedException exception = await Assert.ThrowsAsync<SecurityAdvisoriesFailedException>(() =>
            this.ListJsonAsync(Repeated(1001))
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        SecurityAdvisoriesResult result = await this.ListJsonAsync(Repeated(1000));

        Assert.Equal(expected: 1000, actual: result.Advisories.Count);
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        SecurityAdvisoriesNotFoundException exception = await Assert.ThrowsAsync<SecurityAdvisoriesNotFoundException>(
            () =>
                this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task HttpFailureIsFailedAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(status));

        SecurityAdvisoriesFailedException exception = await Assert.ThrowsAsync<SecurityAdvisoriesFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: status, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":null,"severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5CRP-9R3C-P9VR","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9v","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr\n","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-abcd-9r3c-p9vr","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"CVE-2024-1234","severity":"high","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"severe","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"High","state":"published"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"high"}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"high","state":null}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"high","state":""}]""")]
    [InlineData("""[{"ghsa_id":"GHSA-5crp-9r3c-p9vr","severity":"high","state":"open"}]""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        SecurityAdvisoriesFailedException exception = await Assert.ThrowsAsync<SecurityAdvisoriesFailedException>(() =>
            this.ListAsync(upstream)
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

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, repository: repository)
        );

        Assert.Contains(
            expectedSubstring: "repository",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Empty(upstream.Requests);
    }
}
