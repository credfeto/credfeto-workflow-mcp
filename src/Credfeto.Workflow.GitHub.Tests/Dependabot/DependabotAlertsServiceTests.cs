using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Dependabot;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Dependabot;

public sealed class DependabotAlertsServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string ALERTS_URL = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/dependabot/alerts?";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string DISABLED = "Dependabot alerts are disabled for credfeto/credfeto-workflow-mcp";
    private const string PERMISSION =
        "the caller's token is not allowed to read the Dependabot alerts of credfeto/credfeto-workflow-mcp; it needs read access to Dependabot alerts, or the security_events scope for a classic token";
    private const string FAILED = "could not list the Dependabot alerts of credfeto/credfeto-workflow-mcp";

    private static string Alert(
        int number = 7,
        string state = "\"open\"",
        string severity = "\"high\"",
        string package = "\"Newtonsoft.Json\"",
        string firstPatched = "{\"identifier\":\"13.0.1\"}"
    )
    {
        return "{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"state\":"
            + state
            + ",\"dependency\":{\"package\":{\"ecosystem\":\"nuget\",\"name\":"
            + package
            + "},\"manifest_path\":\"src/App/App.csproj\"}"
            + ",\"security_advisory\":{\"ghsa_id\":\"GHSA-5crp-9r3c-p9vr\",\"severity\":"
            + severity
            + "}"
            + ",\"security_vulnerability\":{\"vulnerable_version_range\":\"< 13.0.1\",\"first_patched_version\":"
            + firstPatched
            + "}}";
    }

    private static string List(params string[] alerts)
    {
        return "[" + string.Join(separator: ',', value: alerts) + "]";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private DependabotAlertsService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<DependabotAlertsResult> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string? state = null,
        string? severity = null,
        int limit = DependabotAlertsQuery.DEFAULT_LIMIT
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(
                repository: repository,
                state: state,
                severity: severity,
                limit: limit,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<DependabotAlertsResult> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    private async Task<Uri?> RequestUriAsync(string? state = null, string? severity = null, int limit = 30)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(List()));

        _ = await this.ListAsync(upstream: upstream, state: state, severity: severity, limit: limit);

        return Assert.Single(upstream.Requests).RequestUri;
    }

    private async Task<TException> ForbiddenAsync<TException>(string message)
        where TException : Exception
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.Forbidden, json: "{\"message\":\"" + message + "\"}")
        );

        return await Assert.ThrowsAsync<TException>(() => this.ListAsync(upstream));
    }

    [Fact]
    public async Task ReadsTheOpenAlertsOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(List(Alert())));

        DependabotAlertsResult result = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Equal(
            expected:
            [
                new DependabotAlertEntry(
                    Number: 7,
                    Severity: "high",
                    State: "open",
                    Ecosystem: "nuget",
                    Package: "Newtonsoft.Json",
                    ManifestPath: "src/App/App.csproj",
                    VulnerableRange: "< 13.0.1",
                    FirstPatchedVersion: "13.0.1",
                    GhsaId: "GHSA-5crp-9r3c-p9vr"
                ),
            ],
            actual: result.Alerts
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: new Uri(ALERTS_URL + "state=open&per_page=30"), actual: request.RequestUri);
    }

    [Fact]
    public async Task NoAlertsIsEmptyAsync()
    {
        DependabotAlertsResult result = await this.ListJsonAsync(List());

        Assert.Equal(expected: REPOSITORY, actual: result.Repository);
        Assert.Empty(result.Alerts);
    }

    [Theory]
    [InlineData("open", "state=open&per_page=30")]
    [InlineData("fixed", "state=fixed&per_page=30")]
    [InlineData("dismissed", "state=dismissed&per_page=30")]
    [InlineData("auto_dismissed", "state=auto_dismissed&per_page=30")]
    [InlineData("all", "per_page=30")]
    [InlineData("", "state=open&per_page=30")]
    public async Task StateIsSentAsAFilterAsync(string state, string query)
    {
        Uri? requestUri = await this.RequestUriAsync(state: state);

        Assert.Equal(expected: new Uri(ALERTS_URL + query), actual: requestUri);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    public async Task SeverityIsSentAsAFilterAsync(string severity)
    {
        Uri? requestUri = await this.RequestUriAsync(severity: severity);

        Assert.Equal(
            expected: new Uri(ALERTS_URL + "state=open&severity=" + severity + "&per_page=30"),
            actual: requestUri
        );
    }

    [Fact]
    public async Task EmptySeverityIsNotSentAsync()
    {
        Uri? requestUri = await this.RequestUriAsync(severity: string.Empty);

        Assert.Equal(expected: new Uri(ALERTS_URL + "state=open&per_page=30"), actual: requestUri);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task LimitIsSentAsThePageSizeAsync(int limit)
    {
        Uri? requestUri = await this.RequestUriAsync(limit: limit);

        Assert.Equal(
            expected: new Uri(ALERTS_URL + "state=open&per_page=" + limit.ToString(CultureInfo.InvariantCulture)),
            actual: requestUri
        );
    }

    [Fact]
    public async Task NoMoreThanTheLimitIsListedInGitHubOrderAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(List(Alert(number: 9), Alert(number: 3), Alert(number: 5)))
        );

        DependabotAlertsResult result = await this.ListAsync(upstream: upstream, limit: 2);

        Assert.Equal(expected: [9, 3], actual: result.Alerts.Select(alert => alert.Number));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("fixed")]
    [InlineData("dismissed")]
    [InlineData("auto_dismissed")]
    public async Task EveryDocumentedStateIsListedAsync(string state)
    {
        DependabotAlertsResult result = await this.ListJsonAsync(List(Alert(state: "\"" + state + "\"")));

        Assert.Equal(expected: state, actual: Assert.Single(result.Alerts).State);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    public async Task EveryDocumentedSeverityIsListedAsync(string severity)
    {
        DependabotAlertsResult result = await this.ListJsonAsync(List(Alert(severity: "\"" + severity + "\"")));

        Assert.Equal(expected: severity, actual: Assert.Single(result.Alerts).Severity);
    }

    [Fact]
    public async Task NoPatchedVersionIsShownAsPlaceholderAsync()
    {
        DependabotAlertsResult result = await this.ListJsonAsync(List(Alert(firstPatched: "null")));

        Assert.Equal(expected: "-", actual: Assert.Single(result.Alerts).FirstPatchedVersion);
    }

    [Fact]
    public async Task MissingDetailIsShownAsPlaceholderAsync()
    {
        DependabotAlertsResult result = await this.ListJsonAsync(
            """[{"number":4,"state":"open","dependency":{"package":{"name":"lodash"}},"security_advisory":{"severity":"low"}}]"""
        );

        Assert.Equal(
            expected: new DependabotAlertEntry(
                Number: 4,
                Severity: "low",
                State: "open",
                Ecosystem: "-",
                Package: "lodash",
                ManifestPath: "-",
                VulnerableRange: "-",
                FirstPatchedVersion: "-",
                GhsaId: "-"
            ),
            actual: Assert.Single(result.Alerts)
        );
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        DependabotAlertsNotFoundException exception = await Assert.ThrowsAsync<DependabotAlertsNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task DisabledAlertsAreReportedAsDisabledAsync()
    {
        DependabotAlertsDisabledException exception = await this.ForbiddenAsync<DependabotAlertsDisabledException>(
            "Dependabot alerts are disabled for this repository."
        );

        Assert.Equal(expected: DISABLED, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Forbidden, actual: inner.StatusCode);
    }

    [Fact]
    public async Task MissingPermissionIsReportedAsPermissionAsync()
    {
        DependabotAlertsPermissionException exception = await this.ForbiddenAsync<DependabotAlertsPermissionException>(
            "Resource not accessible by integration"
        );

        Assert.Equal(expected: PERMISSION, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Forbidden, actual: inner.StatusCode);
    }

    [Fact]
    public async Task OtherForbiddenIsFailedAsync()
    {
        DependabotAlertsFailedException exception = await this.ForbiddenAsync<DependabotAlertsFailedException>(
            "Must have admin rights to Repository."
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        DependabotAlertsFailedException exception = await Assert.ThrowsAsync<DependabotAlertsFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"state":"open","dependency":{"package":{"name":"a"}},"security_advisory":{"severity":"low"}}]""")]
    [InlineData(
        """[{"number":0,"state":"open","dependency":{"package":{"name":"a"}},"security_advisory":{"severity":"low"}}]"""
    )]
    [InlineData("""[{"number":1,"state":"open","security_advisory":{"severity":"low"}}]""")]
    [InlineData(
        """[{"number":1,"state":"open","dependency":{"package":{"name":" "}},"security_advisory":{"severity":"low"}}]"""
    )]
    [InlineData("""[{"number":1,"state":"open","dependency":{"package":{"name":"a"}}}]""")]
    [InlineData(
        """[{"number":1,"state":"open","dependency":{"package":{"name":"a"}},"security_advisory":{"severity":"LOW"}}]"""
    )]
    [InlineData("""[{"number":1,"dependency":{"package":{"name":"a"}},"security_advisory":{"severity":"low"}}]""")]
    [InlineData(
        """[{"number":1,"state":"closed","dependency":{"package":{"name":"a"}},"security_advisory":{"severity":"low"}}]"""
    )]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        DependabotAlertsFailedException exception = await Assert.ThrowsAsync<DependabotAlertsFailedException>(() =>
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

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("OPEN")]
    [InlineData("open&severity=low")]
    public async Task InvalidStateIsRejectedWithoutRequestAsync(string state)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, state: state)
        );

        Assert.Equal(expected: "state", actual: exception.ParameterName);
        Assert.Equal(expected: "it must be open, fixed, dismissed, auto_dismissed or all", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("moderate")]
    [InlineData("HIGH")]
    [InlineData("high,critical")]
    public async Task InvalidSeverityIsRejectedWithoutRequestAsync(string severity)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, severity: severity)
        );

        Assert.Equal(expected: "severity", actual: exception.ParameterName);
        Assert.Equal(expected: "it must be low, medium, high or critical", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidLimitIsRejectedWithoutRequestAsync(int limit)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ListAsync(upstream: upstream, limit: limit)
        );

        Assert.Equal(expected: "limit", actual: exception.ParameterName);
        Assert.Equal(expected: "it must be a whole number from 1 to 100", actual: exception.Rule);
        Assert.Empty(upstream.Requests);
    }
}
