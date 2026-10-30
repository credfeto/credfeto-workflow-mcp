using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Branches;

public sealed class RequiredChecksReaderTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string BRANCH = "main";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    // Mirrors the key the production code stores the upstream message under on a failed request
    private const string UPSTREAM_MESSAGE_KEY = "Credfeto.Workflow.GitHub.UpstreamMessage";
    private const string UNREADABLE = "could not read the required checks of " + REPOSITORY + " branch " + BRANCH;

    private const string RULESETS_UNAVAILABLE =
        """{"message":"Upgrade to GitHub Pro or make this repository public to enable this feature.","documentation_url":"https://docs.github.com/rest","status":"403"}""";

    private const string PROTECTED_BRANCH =
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
        + SHA
        + "\"},\"protection\":{\"required_status_checks\":{\"enforcement_level\":\"non_admins\",\"contexts\":[\"build\",\"legacy\"],\"checks\":[{\"context\":\"build\",\"app_id\":15368},{\"context\":\"any\",\"app_id\":-1},{\"context\":\"unset\",\"app_id\":null}]}}}";

    private const string UNPROTECTED_BRANCH =
        "{\"name\":\"main\",\"commit\":{\"sha\":\"" + SHA + "\"},\"protection\":{\"enabled\":false}}";

    private const string RULES =
        """[{"type":"pull_request","parameters":{}},{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"build","integration_id":15368},{"context":"lint","integration_id":null}]}}]""";

    private const string NO_RULES = "[]";

    private const string REPOSITORY_BODY = """{"full_name":"credfeto/credfeto-workflow-mcp"}""";

    private static readonly Uri BranchUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/branches/" + BRANCH
    );

    private static readonly Uri RulesUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/rules/branches/" + BRANCH + "?per_page=100"
    );

    private static readonly Uri RepositoryUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY);

    private RequiredChecksReader CreateReader(HttpMessageHandler upstream)
    {
        GitHubRestClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: client);
    }

    private static RecordingHttpMessageHandler Respond(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => responses[index]());
    }

    private static string Mutate(string source, string oldValue, string newValue)
    {
        // Guards against a typo in the test silently leaving the payload unchanged
        Assert.Contains(expectedSubstring: oldValue, actualString: source, comparisonType: StringComparison.Ordinal);

        return source.Replace(oldValue: oldValue, newValue: newValue, comparisonType: StringComparison.Ordinal);
    }

    private static string BranchWithRequiredStatusChecks(string requiredStatusChecks)
    {
        return "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":"
            + requiredStatusChecks
            + "}}";
    }

    private static string RulesWithChecks(string checks)
    {
        return "[{\"type\":\"required_status_checks\",\"parameters\":{\"required_status_checks\":" + checks + "}}]";
    }

    [Fact]
    public async Task ReadRequiredChecksMergesProtectionLegacyContextsAndRulesetsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(PROTECTED_BRANCH),
            () => Responses.Json(RULES)
        );

        IReadOnlyList<RequiredCheck> checks = await this.CreateReader(upstream)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(
            expected:
            [
                new RequiredCheck(Name: "build", AppId: 15368),
                new RequiredCheck(Name: "any", AppId: null),
                new RequiredCheck(Name: "unset", AppId: null),
                new RequiredCheck(Name: "legacy", AppId: null),
                new RequiredCheck(Name: "lint", AppId: null),
            ],
            actual: checks
        );

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: BranchUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: RulesUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task ReadRequiredChecksFollowsRulesetPagesAsync()
    {
        const string nextPage =
            GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/rules/branches/" + BRANCH + "?per_page=100&page=2";

        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Json(json: RULES, nextLink: "<" + nextPage + ">; rel=\"next\""),
            () => Responses.Json(RulesWithChecks("""[{"context":"page-two","integration_id":7}]"""))
        );

        IReadOnlyList<RequiredCheck> checks = await this.CreateReader(upstream)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(
            expected:
            [
                new RequiredCheck(Name: "build", AppId: 15368),
                new RequiredCheck(Name: "lint", AppId: null),
                new RequiredCheck(Name: "page-two", AppId: 7),
            ],
            actual: checks
        );

        Assert.Equal(expected: new Uri(nextPage), actual: upstream.Requests[2].RequestUri);
    }

    [Fact]
    public async Task ReadRequiredChecksEncodesBranchPathAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Json(NO_RULES)
        );

        IReadOnlyList<RequiredCheck> checks = await this.CreateReader(upstream)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: "feature/x#1",
                cancellationToken: this.CancellationToken()
            );

        Assert.Empty(checks);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/branches/feature/x%231"),
            actual: upstream.Requests[0].RequestUri
        );
        Assert.Equal(
            expected: new Uri(
                GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/rules/branches/feature/x%231?per_page=100"
            ),
            actual: upstream.Requests[1].RequestUri
        );
    }

    [Theory]
    [InlineData(UNPROTECTED_BRANCH)]
    [InlineData("{\"name\":\"main\",\"commit\":{\"sha\":\"" + SHA + "\"}}")]
    [InlineData("{\"name\":\"main\",\"commit\":{\"sha\":\"" + SHA + "\"},\"protection\":null}")]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\"" + SHA + "\"},\"protection\":{\"required_status_checks\":null}}"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"enforcement_level\":\"off\",\"contexts\":[\"build\"],\"checks\":[{\"context\":\"build\",\"app_id\":1}]}}}"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"enforcement_level\":\"everyone\",\"contexts\":null,\"checks\":null}}}"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"enforcement_level\":\"everyone\"}}}"
    )]
    public async Task ReadBranchWithoutRequiredChecksReturnsNoChecksAsync(string branchBody)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(branchBody));

        BranchInfo branchInfo = await this.CreateReader(upstream)
            .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: SHA, actual: branchInfo.Sha);
        Assert.Empty(branchInfo.Checks);
    }

    [Fact]
    public async Task ReadBranchReturnsShaAndProtectionChecksWithOneRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(PROTECTED_BRANCH));

        BranchInfo branchInfo = await this.CreateReader(upstream)
            .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: SHA, actual: branchInfo.Sha);
        Assert.Equal(
            expected:
            [
                new RequiredCheck(Name: "build", AppId: 15368),
                new RequiredCheck(Name: "any", AppId: null),
                new RequiredCheck(Name: "unset", AppId: null),
                new RequiredCheck(Name: "legacy", AppId: null),
            ],
            actual: branchInfo.Checks
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: BranchUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task ReadBranchKeepsOnlyLegacyContextsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(
                BranchWithRequiredStatusChecks("""{"enforcement_level":"everyone","contexts":["one","two"]}""")
            )
        );

        BranchInfo branchInfo = await this.CreateReader(upstream)
            .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken());

        Assert.Equal(
            expected: [new RequiredCheck(Name: "one", AppId: null), new RequiredCheck(Name: "two", AppId: null)],
            actual: branchInfo.Checks
        );
    }

    [Theory]
    [InlineData("{\"name\":\"main\",\"protection\":null}", "GitHub returned a branch without a head commit")]
    [InlineData("{\"name\":\"main\",\"commit\":null}", "GitHub returned a branch without a head commit")]
    [InlineData("{\"name\":\"main\",\"commit\":{\"sha\":null}}", "GitHub returned a branch without a head commit")]
    [InlineData("{\"name\":\"main\",\"commit\":{\"sha\":\"\"}}", "GitHub returned a branch without a head commit")]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"checks\":[null]}}}",
        "GitHub returned an empty required status check"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"checks\":[{\"context\":\"\",\"app_id\":1}]}}}",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"checks\":[{\"context\":null,\"app_id\":1}]}}}",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"contexts\":[null]}}}",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"contexts\":[\"\"]}}}",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        "{\"name\":\"main\",\"commit\":{\"sha\":\""
            + SHA
            + "\"},\"protection\":{\"required_status_checks\":{\"checks\":[{\"context\":\"build\",\"app_id\":-2}]}}}",
        "GitHub returned a required status check with an invalid app id"
    )]
    public async Task ReadBranchWithInvalidResponseIsUnreadableAsync(string branchBody, string innerMessage)
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(branchBody));

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: innerMessage, actual: inner.Message);
    }

    [Theory]
    [InlineData(
        """[{"type":"required_status_checks"}]""",
        "GitHub returned a required status checks rule without its checks"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":null}]""",
        "GitHub returned a required status checks rule without its checks"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":{"required_status_checks":null}}]""",
        "GitHub returned a required status checks rule without its checks"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":{"required_status_checks":[null]}}]""",
        "GitHub returned an empty required status check"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"","integration_id":1}]}}]""",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":{"required_status_checks":[{"integration_id":1}]}}]""",
        "GitHub returned a required status check without a name"
    )]
    [InlineData(
        """[{"type":"required_status_checks","parameters":{"required_status_checks":[{"context":"build","integration_id":-5}]}}]""",
        "GitHub returned a required status check with an invalid app id"
    )]
    public async Task ReadRequiredChecksWithInvalidRulesIsUnreadableAsync(string rulesBody, string innerMessage)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Json(rulesBody)
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
        GitHubInvalidResponseException inner = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Equal(expected: innerMessage, actual: inner.Message);
    }

    [Fact]
    public async Task ReadRequiredChecksIgnoresOtherAndEmptyRulesAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Json("""[null,{"type":null},{"type":"deletion","parameters":null}]""")
        );

        IReadOnlyList<RequiredCheck> checks = await this.CreateReader(upstream)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Empty(checks);
    }

    [Fact]
    public async Task ReadRequiredChecksTreatsRulesetsUnavailableAsNoRulesetsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(PROTECTED_BRANCH),
            () => Responses.StatusWithJson(statusCode: HttpStatusCode.Forbidden, json: RULESETS_UNAVAILABLE)
        );

        IReadOnlyList<RequiredCheck> checks = await this.CreateReader(upstream)
            .ReadRequiredChecksAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(
            expected:
            [
                new RequiredCheck(Name: "build", AppId: 15368),
                new RequiredCheck(Name: "any", AppId: null),
                new RequiredCheck(Name: "unset", AppId: null),
                new RequiredCheck(Name: "legacy", AppId: null),
            ],
            actual: checks
        );
    }

    public static TheoryData<string> ForbiddenRulesetBodies()
    {
        return
        [
            "",
            "not json",
            "null",
            """{"message":null}""",
            """{"message":"Resource not accessible by integration"}""",
            "{\"message\":\"Upgrade to GitHub Pro or make this repository public\",\"padding\":\""
                + new string(c: 'x', count: 16 * 1024)
                + "\"}",
        ];
    }

    [Theory]
    [MemberData(nameof(ForbiddenRulesetBodies))]
    public async Task ReadRequiredChecksWithOtherForbiddenRulesetsIsUnreadableAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.StatusWithJson(statusCode: HttpStatusCode.Forbidden, json: body)
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Forbidden, actual: inner.StatusCode);
    }

    [Fact]
    public async Task ReadRequiredChecksWithForbiddenRulesetsWithoutBodyIsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Status(HttpStatusCode.Forbidden)
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Null(inner.Data[UPSTREAM_MESSAGE_KEY] as string);
    }

    [Fact]
    public async Task ReadRequiredChecksKeepsUpstreamMessageOutOfExceptionMessageAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () =>
                Responses.StatusWithJson(
                    statusCode: HttpStatusCode.Forbidden,
                    json: """{"message":"secret upstream detail"}"""
                )
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: "secret upstream detail", actual: inner.Data[UPSTREAM_MESSAGE_KEY] as string);
        Assert.DoesNotContain(
            expectedSubstring: "secret",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "secret",
            actualString: inner.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReadRequiredChecksForMissingBranchIsBranchNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Status(HttpStatusCode.NotFound),
            () => Responses.Json(REPOSITORY_BODY)
        );

        BranchNotFoundException exception = await Assert.ThrowsAsync<BranchNotFoundException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: "the branch main was not found in " + REPOSITORY, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);

        // The rulesets are never asked for once the branch is missing
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: BranchUri, actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: RepositoryUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task ReadBranchForMissingRepositoryIsRepositoryNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Status(HttpStatusCode.NotFound),
            () => Responses.Status(HttpStatusCode.NotFound)
        );

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.CreateReader(upstream)
                .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(
            expected: "the repository " + REPOSITORY + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadBranchWhenRepositoryCheckFailsIsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Status(HttpStatusCode.NotFound),
            () => Responses.Status(HttpStatusCode.Unauthorized)
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadBranchAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Unauthorized, actual: inner.StatusCode);
    }

    [Fact]
    public async Task ReadRequiredChecksWhenBranchFailsDoesNotReadRulesetsAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Status(HttpStatusCode.Unauthorized));

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task ReadRequiredChecksWithUnreadableBranchBodyIsUnreadableAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json("not json"));

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadRequiredCheckNamesReturnsSortedDistinctNamesAsync()
    {
        string branch = BranchWithRequiredStatusChecks(
            """{"enforcement_level":"everyone","contexts":["zeta","Alpha"],"checks":[{"context":"zeta","app_id":1},{"context":"beta","app_id":2}]}"""
        );
        string rules = RulesWithChecks(
            """[{"context":"zeta","integration_id":3},{"context":"alpha","integration_id":null},{"context":"line\nbreak","integration_id":null}]"""
        );

        using RecordingHttpMessageHandler upstream = Respond(() => Responses.Json(branch), () => Responses.Json(rules));

        BranchRequiredChecksResponse response = await this.CreateReader(upstream)
            .ReadRequiredCheckNamesAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: REPOSITORY, actual: response.Repo);
        Assert.Equal(expected: BRANCH, actual: response.Branch);
        Assert.Equal(expected: ["Alpha", "alpha", "beta", "line break", "zeta"], actual: response.Checks);
    }

    [Fact]
    public async Task ReadRequiredCheckNamesMergesNamesThatOnlyDifferByControlCharactersAsync()
    {
        string branch = BranchWithRequiredStatusChecks(
            """{"enforcement_level":"everyone","checks":[{"context":"a\tb","app_id":1},{"context":"a b","app_id":2}]}"""
        );

        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(branch),
            () => Responses.Json(NO_RULES)
        );

        BranchRequiredChecksResponse response = await this.CreateReader(upstream)
            .ReadRequiredCheckNamesAsync(
                repository: REPOSITORY,
                branch: BRANCH,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: ["a b"], actual: response.Checks);
    }

    [Fact]
    public async Task ReadRequiredCheckNamesWhenUnreadablePropagatesAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(() =>
            Responses.Json(Mutate(source: PROTECTED_BRANCH, oldValue: "\"app_id\":-1", newValue: "\"app_id\":-3"))
        );

        RequiredChecksUnreadableException exception = await Assert.ThrowsAsync<RequiredChecksUnreadableException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredCheckNamesAsync(
                    repository: REPOSITORY,
                    branch: BRANCH,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: UNREADABLE, actual: exception.Message);
    }

    [Theory]
    [InlineData("", BRANCH, "repository")]
    [InlineData("no-slash", BRANCH, "repository")]
    [InlineData("owner/repo/extra", BRANCH, "repository")]
    [InlineData("owner/re po", BRANCH, "repository")]
    [InlineData(REPOSITORY, "", "branch")]
    [InlineData(REPOSITORY, "-bad", "branch")]
    [InlineData(REPOSITORY, "HEAD", "branch")]
    [InlineData(REPOSITORY, "bad..name", "branch")]
    [InlineData(REPOSITORY, "bad name", "branch")]
    public async Task ReadWithInvalidArgumentsMakesNoRequestAsync(string repository, string branch, string parameter)
    {
        using RecordingHttpMessageHandler upstream = Respond();

        ParameterValidationException branchException = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateReader(upstream)
                .ReadBranchAsync(repository: repository, branch: branch, cancellationToken: this.CancellationToken())
                .AsTask()
        );
        ParameterValidationException checksException = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(
                    repository: repository,
                    branch: branch,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );
        ParameterValidationException namesException = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredCheckNamesAsync(
                    repository: repository,
                    branch: branch,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Equal(expected: parameter, actual: branchException.ParameterName);
        Assert.Equal(expected: parameter, actual: checksException.ParameterName);
        Assert.Equal(expected: parameter, actual: namesException.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ReadRequiredChecksWhenCancelledIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = Respond(
            () => Responses.Json(UNPROTECTED_BRANCH),
            () => Responses.Json(NO_RULES)
        );

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateReader(upstream)
                .ReadRequiredChecksAsync(repository: REPOSITORY, branch: BRANCH, cancellationToken: cancelled.Token)
                .AsTask()
        );
    }
}
