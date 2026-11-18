using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Repositories;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Repositories;

public sealed class RepoViewServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FAILED = "could not view the repository credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";

    private static string Repository(
        string defaultBranchRef = "{\"name\":\"main\"}",
        string description = "\"A workflow MCP server\"",
        string visibility = "\"PUBLIC\"",
        string mergeCommit = "true",
        string squashMerge = "false",
        string rebaseMerge = "true"
    )
    {
        return "{\"data\":{\"repository\":{\"defaultBranchRef\":"
            + defaultBranchRef
            + ",\"description\":"
            + description
            + ",\"visibility\":"
            + visibility
            + ",\"mergeCommitAllowed\":"
            + mergeCommit
            + ",\"squashMergeAllowed\":"
            + squashMerge
            + ",\"rebaseMergeAllowed\":"
            + rebaseMerge
            + "}}}";
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private RepoViewService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubGraphQlClient graphQl = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQl);
    }

    private async Task<RepoViewResult> ViewAsync(RecordingHttpMessageHandler upstream, string repository = REPOSITORY)
    {
        return await this.CreateService(upstream)
            .ViewAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<RepoViewResult> ViewAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ViewAsync(upstream);
    }

    private async Task<RepoViewFailedException> ViewFailedAsync(RecordingHttpMessageHandler upstream)
    {
        RepoViewFailedException exception = await Assert.ThrowsAsync<RepoViewFailedException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);

        return exception;
    }

    private async Task<RepositoryNotFoundException> ViewNotFoundAsync(RecordingHttpMessageHandler upstream)
    {
        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ViewAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);

        return exception;
    }

    [Fact]
    public async Task ProjectsEveryFieldAsync()
    {
        RepoViewResult result = await this.ViewAsync(Repository());

        Assert.Equal(
            expected: new RepoViewResult(
                DefaultBranch: "main",
                Visibility: "PUBLIC",
                AllowMergeCommit: true,
                AllowSquashMerge: false,
                AllowRebaseMerge: true,
                Description: "A workflow MCP server"
            ),
            actual: result
        );
    }

    [Fact]
    public async Task TheQueryAsksForTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Repository()));

        _ = await this.ViewAsync(upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("r").GetString());
        Assert.Equal(
            expected: "query($o:String!,$r:String!){repository(owner:$o,name:$r){defaultBranchRef{name} description visibility mergeCommitAllowed squashMergeAllowed rebaseMergeAllowed}}",
            actual: body.RootElement.GetProperty("query").GetString()
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    public async Task AnEmptyRepositoryHasNoDefaultBranchAsync(string defaultBranchRef)
    {
        RepoViewResult result = await this.ViewAsync(Repository(defaultBranchRef: defaultBranchRef));

        Assert.Equal(expected: string.Empty, actual: result.DefaultBranch);
    }

    [Fact]
    public async Task NullMergeMethodsStayNullAsync()
    {
        RepoViewResult result = await this.ViewAsync(
            Repository(mergeCommit: "null", squashMerge: "null", rebaseMerge: "null")
        );

        Assert.Null(result.AllowMergeCommit);
        Assert.Null(result.AllowSquashMerge);
        Assert.Null(result.AllowRebaseMerge);
    }

    [Fact]
    public async Task MissingMergeMethodsAreNullAsync()
    {
        RepoViewResult result = await this.ViewAsync(
            """{"data":{"repository":{"defaultBranchRef":{"name":"main"},"description":null,"visibility":"PRIVATE"}}}"""
        );

        Assert.Equal(
            expected: new RepoViewResult(
                DefaultBranch: "main",
                Visibility: "PRIVATE",
                AllowMergeCommit: null,
                AllowSquashMerge: null,
                AllowRebaseMerge: null,
                Description: string.Empty
            ),
            actual: result
        );
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task ANullDescriptionIsEmptyAsync(string description)
    {
        RepoViewResult result = await this.ViewAsync(Repository(description: description));

        Assert.Equal(expected: string.Empty, actual: result.Description);
    }

    [Fact]
    public async Task ControlCharactersBecomeSpacesAsync()
    {
        RepoViewResult result = await this.ViewAsync(
            Repository(defaultBranchRef: "{\"name\":\"ma\\tin\"}", description: "\"first\\r\\nsecond\\u001b[31m\"")
        );

        Assert.Equal(expected: "ma in", actual: result.DefaultBranch);
        Assert.Equal(expected: "first  second [31m", actual: result.Description);
    }

    [Theory]
    [InlineData("\"PUBLIC\"", "PUBLIC")]
    [InlineData("\"PRIVATE\"", "PRIVATE")]
    [InlineData("\"INTERNAL\"", "INTERNAL")]
    [InlineData("\"SOME_NEW_VALUE\"", "SOME_NEW_VALUE")]
    public async Task VisibilityIsGitHubsOwnValueAsync(string visibility, string expected)
    {
        RepoViewResult result = await this.ViewAsync(Repository(visibility: visibility));

        Assert.Equal(expected: expected, actual: result.Visibility);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"public\"")]
    [InlineData("\"PUBLIC \"")]
    [InlineData("\"PUBLIC\\nFORGED\"")]
    [InlineData("\"PUBLIC1\"")]
    public async Task AnInvalidVisibilityIsFailedAsync(string visibility)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Repository(visibility: visibility)));

        RepoViewFailedException exception = await this.ViewFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task AMissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"repository":null},"errors":[{"type":"NOT_FOUND","message":"Could not resolve to a Repository with the name 'credfeto/credfeto-workflow-mcp'."}]}"""
            )
        );

        RepositoryNotFoundException exception = await this.ViewNotFoundAsync(upstream);

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task ANullRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repository":null}}""")
        );

        RepositoryNotFoundException exception = await this.ViewNotFoundAsync(upstream);

        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task HttpNotFoundIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await this.ViewNotFoundAsync(upstream);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task GraphQlErrorIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Something went wrong"}],"data":null}""")
        );

        RepoViewFailedException exception = await this.ViewFailedAsync(upstream);

        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        RepoViewFailedException exception = await this.ViewFailedAsync(upstream);

        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        RepoViewFailedException exception = await this.ViewFailedAsync(upstream);

        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task NullDataIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"data":null}"""));

        _ = await this.ViewFailedAsync(upstream);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    [InlineData("credfeto/bad name")]
    public async Task InvalidRepositoryIsRejectedWithoutRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ViewAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
