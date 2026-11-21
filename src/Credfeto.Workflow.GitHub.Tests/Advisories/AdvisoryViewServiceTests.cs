using System;
using System.Globalization;
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

public sealed class AdvisoryViewServiceTests : GitHubClientTestBase
{
    private const string GHSA = "GHSA-jfh8-c2jp-5v3q";
    private const string PUBLISHED = "2021-12-10T00:40:56Z";
    private const string WITHDRAWN = "2022-01-05T12:00:00Z";
    private const string FAILED = "could not read the security advisory " + GHSA;

    private const string NUGET_PACKAGE =
        "{\"package\":{\"ecosystem\":\"nuget\",\"name\":\"Example.Package\"},\"vulnerable_version_range\":\"< 1.2.3\",\"first_patched_version\":\"1.2.3\"}";

    private const string MAVEN_PACKAGE =
        "{\"package\":{\"ecosystem\":\"maven\",\"name\":\"org.example:core\"},\"vulnerable_version_range\":\">= 2.0.0, < 2.15.0\",\"first_patched_version\":null}";

    private static string AdvisoryJson(
        string ghsaId = GHSA,
        string severity = "critical",
        string withdrawn = "null",
        string vulnerabilities = "[" + NUGET_PACKAGE + "]"
    )
    {
        return "{\"ghsa_id\":\""
            + ghsaId
            + "\",\"severity\":\""
            + severity
            + "\",\"summary\":\"Remote code injection\",\"published_at\":\""
            + PUBLISHED
            + "\",\"withdrawn_at\":"
            + withdrawn
            + ",\"vulnerabilities\":"
            + vulnerabilities
            + "}";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Answering(string json)
    {
        return new(index => index == 0 ? Responses.Json(json) : Unexpected(index));
    }

    private AdvisoryViewService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<AdvisoryViewResult> ViewAsync(RecordingHttpMessageHandler upstream, string ghsa = GHSA)
    {
        return await this.CreateService(upstream).ViewAsync(ghsa: ghsa, cancellationToken: this.CancellationToken());
    }

    private async Task<AdvisoryViewFailedException> ViewFailsAsync(RecordingHttpMessageHandler upstream)
    {
        AdvisoryViewFailedException exception = await Assert.ThrowsAsync<AdvisoryViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    [Fact]
    public async Task ReadsTheAdvisoryAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(AdvisoryJson());

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(expected: GHSA, actual: result.GhsaId);
        Assert.Equal(expected: "critical", actual: result.Severity);
        Assert.Equal(expected: "Remote code injection", actual: result.Summary);
        Assert.Equal(expected: PUBLISHED, actual: result.Published);
        Assert.Equal(expected: string.Empty, actual: result.Withdrawn);
        Assert.Equal(
            expected: new(
                Ecosystem: "nuget",
                Package: "Example.Package",
                VulnerableRange: "< 1.2.3",
                FirstPatched: "1.2.3"
            ),
            actual: Assert.Single(result.Packages)
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + "advisories/" + GHSA), actual: request.RequestUri);
    }

    [Fact]
    public async Task ReadsAWithdrawnAdvisoryAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(AdvisoryJson(withdrawn: "\"" + WITHDRAWN + "\""));

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(expected: WITHDRAWN, actual: result.Withdrawn);
    }

    [Fact]
    public async Task ReadsEveryAffectedPackageInOrderAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            AdvisoryJson(vulnerabilities: "[" + NUGET_PACKAGE + "," + MAVEN_PACKAGE + "]")
        );

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(
            expected:
            [
                new(Ecosystem: "nuget", Package: "Example.Package", VulnerableRange: "< 1.2.3", FirstPatched: "1.2.3"),
                new(
                    Ecosystem: "maven",
                    Package: "org.example:core",
                    VulnerableRange: ">= 2.0.0, < 2.15.0",
                    FirstPatched: string.Empty
                ),
            ],
            actual: result.Packages
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task AnAdvisoryWithNoVulnerabilitiesHasNoPackagesAsync(string vulnerabilities)
    {
        using RecordingHttpMessageHandler upstream = Answering(AdvisoryJson(vulnerabilities: vulnerabilities));

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Empty(result.Packages);
    }

    [Fact]
    public async Task MissingPackageDetailsAreEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            AdvisoryJson(
                vulnerabilities: "[{\"package\":null,\"vulnerable_version_range\":null,\"first_patched_version\":null}]"
            )
        );

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(
            expected: new(
                Ecosystem: string.Empty,
                Package: string.Empty,
                VulnerableRange: string.Empty,
                FirstPatched: string.Empty
            ),
            actual: Assert.Single(result.Packages)
        );
    }

    [Fact]
    public async Task MissingTextIsEmptyAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            "{\"ghsa_id\":\"" + GHSA + "\",\"severity\":\"low\",\"vulnerabilities\":[]}"
        );

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(expected: "low", actual: result.Severity);
        Assert.Equal(expected: string.Empty, actual: result.Summary);
        Assert.Equal(expected: string.Empty, actual: result.Published);
        Assert.Equal(expected: string.Empty, actual: result.Withdrawn);
    }

    [Fact]
    public async Task LineBreakingTextFromGitHubIsReplacedAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            "{\"ghsa_id\":\""
                + GHSA
                + "\",\"severity\":\"high\",\"summary\":\"first\\nsecond\",\"published_at\":\""
                + PUBLISHED
                + "\",\"vulnerabilities\":[{\"package\":{\"ecosystem\":\"npm\",\"name\":\"bad\\rname\"}}]}"
        );

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.DoesNotContain(
            expectedSubstring: "\n",
            actualString: result.Summary,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "\r",
            actualString: Assert.Single(result.Packages).Package,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    [InlineData("medium")]
    [InlineData("low")]
    [InlineData("unknown")]
    public async Task EveryDocumentedSeverityIsReadAsync(string severity)
    {
        using RecordingHttpMessageHandler upstream = Answering(AdvisoryJson(severity: severity));

        AdvisoryViewResult result = await this.ViewAsync(upstream);

        Assert.Equal(expected: severity, actual: result.Severity);
    }

    [Theory]
    [InlineData("severe")]
    [InlineData("Critical")]
    [InlineData("")]
    public async Task AnUnknownSeverityIsAFailedReadAsync(string severity)
    {
        using RecordingHttpMessageHandler upstream = Answering(AdvisoryJson(severity: severity));

        AdvisoryViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("{\"ghsa_id\":\"" + GHSA + "\",\"vulnerabilities\":[]}")]
    [InlineData("{\"severity\":\"high\",\"vulnerabilities\":[]}")]
    [InlineData("{\"ghsa_id\":\"GHSA-2222-3333-4444\",\"severity\":\"high\",\"vulnerabilities\":[]}")]
    [InlineData("{\"ghsa_id\":\"ghsa-jfh8-c2jp-5v3q\",\"severity\":\"high\",\"vulnerabilities\":[]}")]
    [InlineData("{\"ghsa_id\":\"" + GHSA + "\",\"severity\":\"high\",\"vulnerabilities\":[null]}")]
    [InlineData("null")]
    public async Task AnInvalidAnswerIsAFailedReadAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Answering(json);

        AdvisoryViewFailedException exception = await this.ViewFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task AMissingAdvisoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(HttpStatusCode.NotFound) : Unexpected(index)
        );

        AdvisoryNotFoundException exception = await Assert.ThrowsAsync<AdvisoryNotFoundException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: "the security advisory " + GHSA + " was not found", actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task AnErrorFromGitHubIsAFailedReadAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(statusCode) : Unexpected(index)
        );

        AdvisoryViewFailedException exception = await this.ViewFailsAsync(upstream);

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: statusCode, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GHSA")]
    [InlineData("ghsa-jfh8-c2jp-5v3q")]
    [InlineData("GHSA-jfh8-c2jp-5v3q\n")]
    [InlineData("GHSA-jfh8-c2jp-5v3")]
    [InlineData("GHSA-jfh8-c2jp-5v3qq")]
    [InlineData("GHSA-JFH8-C2JP-5V3Q")]
    [InlineData("GHSA-jfh8-c2jp-5v3a")]
    [InlineData("GHSA-jfh8-c2jp-5v31")]
    [InlineData("GHSA_jfh8-c2jp-5v3q")]
    [InlineData("GHSA-jfh8_c2jp-5v3q")]
    [InlineData("GHSA-jfh8-c2jp/5v3q")]
    [InlineData("CVE-2021-44228")]
    [InlineData("../../user")]
    public async Task AMalformedIdIsRefusedWithoutAReadAsync(string ghsa)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ViewAsync(upstream: upstream, ghsa: ghsa)
        );

        Assert.Equal(expected: "ghsa", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
