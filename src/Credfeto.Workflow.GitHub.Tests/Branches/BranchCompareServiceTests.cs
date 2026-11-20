using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Branches;

public sealed class BranchCompareServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string BASE = "main";
    private const string HEAD = "feat/148-compare";
    private const string FAILED = "could not compare main...feat/148-compare in credfeto/credfeto-workflow-mcp";
    private const string REPOSITORY_NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";

    private const string COMPARE_QUERY =
        "query($o:String!,$r:String!,$b:String!,$h:String!){repository(owner:$o,name:$r){ref(qualifiedName:$b){compare(headRef:$h){status aheadBy behindBy}}}}";

    private const string REFS_QUERY =
        "query($o:String!,$r:String!,$b:String!,$h:String!){repository(owner:$o,name:$r){base:ref(qualifiedName:$b){name} head:ref(qualifiedName:$h){name}}}";

    private const string NOT_FOUND_ERROR =
        """{"data":{"repository":{"ref":{"compare":null}}},"errors":[{"type":"NOT_FOUND","message":"Could not resolve head ref 'refs/heads/feat/148-compare'."}]}""";

    private const string REPOSITORY_NOT_FOUND_ERROR =
        """{"data":{"repository":null},"errors":[{"type":"NOT_FOUND","message":"Could not resolve to a Repository with the name 'credfeto/credfeto-workflow-mcp'."}]}""";

    private const string OTHER_ERROR =
        """{"data":null,"errors":[{"type":"FORBIDDEN","message":"Resource not accessible"}]}""";

    private static string Comparison(string status = "\"BEHIND\"", string aheadBy = "0", string behindBy = "3")
    {
        return "{\"data\":{\"repository\":{\"ref\":{\"compare\":{\"status\":"
            + status
            + ",\"aheadBy\":"
            + aheadBy
            + ",\"behindBy\":"
            + behindBy
            + "}}}}}";
    }

    private static string Refs(string baseRef, string headRef)
    {
        return "{\"data\":{\"repository\":{\"base\":" + baseRef + ",\"head\":" + headRef + "}}}";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Answering(string first, string? second = null)
    {
        return new(index =>
            index switch
            {
                0 => Responses.Json(first),
                1 when second is not null => Responses.Json(second),
                _ => Unexpected(index),
            }
        );
    }

    private BranchCompareService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubGraphQlClient graphQl = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQl);
    }

    private async Task<BranchCompareResponse> CompareAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string baseBranch = BASE,
        string headBranch = HEAD
    )
    {
        return await this.CreateService(upstream)
            .CompareAsync(
                repository: repository,
                baseBranch: baseBranch,
                headBranch: headBranch,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task<TException> CompareThrowsAsync<TException>(RecordingHttpMessageHandler upstream, string message)
        where TException : Exception
    {
        TException exception = await Assert.ThrowsAsync<TException>(() => this.CompareAsync(upstream));

        Assert.Equal(expected: message, actual: exception.Message);

        return exception;
    }

    private static void AssertQuery(RecordedRequest request, string query)
    {
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: query, actual: body.RootElement.GetProperty("query").GetString());
        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: "refs/heads/main", actual: variables.GetProperty("b").GetString());
        Assert.Equal(expected: "refs/heads/feat/148-compare", actual: variables.GetProperty("h").GetString());
    }

    [Theory]
    [InlineData("IDENTICAL", 0, 0, BranchCompareStatus.Identical)]
    [InlineData("AHEAD", 2, 0, BranchCompareStatus.Ahead)]
    [InlineData("BEHIND", 0, 3, BranchCompareStatus.Behind)]
    [InlineData("DIVERGED", 4, 5, BranchCompareStatus.Diverged)]
    public async Task ProjectsTheComparisonAsync(string status, int aheadBy, int behindBy, BranchCompareStatus expected)
    {
        using RecordingHttpMessageHandler upstream = Answering(
            Comparison(
                status: "\"" + status + "\"",
                aheadBy: aheadBy.ToString(CultureInfo.InvariantCulture),
                behindBy: behindBy.ToString(CultureInfo.InvariantCulture)
            )
        );

        BranchCompareResponse response = await this.CompareAsync(upstream);

        Assert.Equal(
            expected: new BranchCompareResponse(
                Repo: REPOSITORY,
                Base: BASE,
                Head: HEAD,
                Status: expected,
                AheadBy: aheadBy,
                BehindBy: behindBy
            ),
            actual: response
        );
    }

    [Fact]
    public async Task OneQueryComparesTheQualifiedBranchRefsAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(Comparison());

        _ = await this.CompareAsync(upstream);

        AssertQuery(request: Assert.Single(upstream.Requests), query: COMPARE_QUERY);
    }

    [Fact]
    public async Task AMissingBaseBranchIsNotFoundWithoutASecondQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering("""{"data":{"repository":{"ref":null}}}""");

        await this.CompareThrowsAsync<BranchNotFoundException>(
            upstream: upstream,
            message: "the branch main was not found in " + REPOSITORY
        );

        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AMissingHeadBranchIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            first: NOT_FOUND_ERROR,
            second: Refs(baseRef: "{\"name\":\"main\"}", headRef: "null")
        );

        BranchNotFoundException exception = await this.CompareThrowsAsync<BranchNotFoundException>(
            upstream: upstream,
            message: "the branch feat/148-compare was not found in " + REPOSITORY
        );

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertQuery(request: upstream.Requests[1], query: REFS_QUERY);
    }

    [Fact]
    public async Task ABaseBranchFoundMissingByTheSecondQueryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            first: NOT_FOUND_ERROR,
            second: Refs(baseRef: "null", headRef: "null")
        );

        await this.CompareThrowsAsync<BranchNotFoundException>(
            upstream: upstream,
            message: "the branch main was not found in " + REPOSITORY
        );
    }

    [Fact]
    public async Task AMissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            first: REPOSITORY_NOT_FOUND_ERROR,
            second: REPOSITORY_NOT_FOUND_ERROR
        );

        await this.CompareThrowsAsync<RepositoryNotFoundException>(upstream: upstream, message: REPOSITORY_NOT_FOUND);
    }

    [Fact]
    public async Task ARepositoryTheSecondQueryAnswersAsNullIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            first: NOT_FOUND_ERROR,
            second: """{"data":{"repository":null}}"""
        );

        await this.CompareThrowsAsync<RepositoryNotFoundException>(upstream: upstream, message: REPOSITORY_NOT_FOUND);
    }

    [Fact]
    public async Task ARepositoryTheCompareQueryAnswersAsNullIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering("""{"data":{"repository":null}}""");

        await this.CompareThrowsAsync<RepositoryNotFoundException>(upstream: upstream, message: REPOSITORY_NOT_FOUND);
    }

    [Fact]
    public async Task NotFoundWhenBothBranchesExistIsAFailedReadAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(
            first: NOT_FOUND_ERROR,
            second: Refs(baseRef: "{\"name\":\"main\"}", headRef: "{\"name\":\"feat/148-compare\"}")
        );

        BranchCompareFailedException exception = await this.CompareThrowsAsync<BranchCompareFailedException>(
            upstream: upstream,
            message: FAILED
        );

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task ASecondQueryThatFailsIsAFailedReadAsync()
    {
        using RecordingHttpMessageHandler upstream = Answering(first: NOT_FOUND_ERROR, second: OTHER_ERROR);

        await this.CompareThrowsAsync<BranchCompareFailedException>(upstream: upstream, message: FAILED);
    }

    [Theory]
    [InlineData("""{"data":{"repository":{"ref":{"compare":null}}}}""")]
    [InlineData(OTHER_ERROR)]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":"SOMETHING","aheadBy":0,"behindBy":0}}}}}""")]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":null,"aheadBy":0,"behindBy":0}}}}}""")]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":"BEHIND","aheadBy":null,"behindBy":1}}}}}""")]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":"BEHIND","aheadBy":0,"behindBy":-1}}}}}""")]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":"BEHIND","aheadBy":0}}}}}""")]
    [InlineData("""{"data":{"repository":{"ref":{"compare":{"status":"BEHIND","aheadBy":"x","behindBy":1}}}}}""")]
    public async Task AnUnusableAnswerIsAFailedReadAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Answering(json);

        await this.CompareThrowsAsync<BranchCompareFailedException>(upstream: upstream, message: FAILED);

        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AnHttpFailureIsAFailedReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.BadGateway));

        BranchCompareFailedException exception = await this.CompareThrowsAsync<BranchCompareFailedException>(
            upstream: upstream,
            message: FAILED
        );

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("credfeto", BASE, HEAD, "repository")]
    [InlineData(REPOSITORY, "-main", HEAD, "base")]
    [InlineData(REPOSITORY, BASE, "HEAD", "head")]
    [InlineData(REPOSITORY, BASE, "feat..x", "head")]
    public async Task InvalidArgumentsAreRefusedBeforeAnyReadAsync(
        string repository,
        string baseBranch,
        string headBranch,
        string parameterName
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CompareAsync(
                upstream: upstream,
                repository: repository,
                baseBranch: baseBranch,
                headBranch: headBranch
            )
        );

        Assert.Equal(expected: parameterName, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
