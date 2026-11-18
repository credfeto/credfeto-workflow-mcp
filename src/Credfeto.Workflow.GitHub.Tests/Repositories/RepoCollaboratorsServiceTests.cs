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
using Credfeto.Workflow.GitHub.Repositories;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Repositories;

public sealed class RepoCollaboratorsServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FORBIDDEN =
        "the caller lacks push access to credfeto/credfeto-workflow-mcp, which GitHub requires to list its collaborators";
    private const string FAILED = "could not list the collaborators of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY =
        "credfeto/credfeto-workflow-mcp has more than 1000 collaborators, so none is listed";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/collaborators?per_page=100"
    );

    private static string Collaborator(string login, string role = "write")
    {
        return "{\"login\":\"" + login + "\",\"id\":1,\"type\":\"User\",\"role_name\":\"" + role + "\"}";
    }

    private static string Page(params string[] collaborators)
    {
        return "[" + string.Join(separator: ',', values: collaborators) + "]";
    }

    private static string Numbered(int count)
    {
        return Page([
            .. Enumerable
                .Range(start: 1, count: count)
                .Select(number => Collaborator("user-" + number.ToString(CultureInfo.InvariantCulture))),
        ]);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private RepoCollaboratorsService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<RepoCollaboratorsResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<RepoCollaboratorsResponse> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    private async Task<RepoCollaboratorsFailedException> FailsWithStatusAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(status));

        return await Assert.ThrowsAsync<RepoCollaboratorsFailedException>(() => this.ListAsync(upstream));
    }

    [Fact]
    public async Task ReadsTheCollaboratorsOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json(Page(Collaborator(login: "credfeto", role: "admin")))
        );

        RepoCollaboratorsResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(
            expected: [new RepoCollaborator(Login: "credfeto", Role: "admin")],
            actual: response.Collaborators
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task NoCollaboratorsIsEmptyAsync()
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.Collaborators);
    }

    [Fact]
    public async Task CollaboratorsAreSortedOrdinallyByLoginAsync()
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(
            Page(Collaborator("zebra"), Collaborator("alpha"), Collaborator("Beta"), Collaborator("_under"))
        );

        Assert.Equal(
            expected: ["Beta", "_under", "alpha", "zebra"],
            actual: response.Collaborators.Select(collaborator => collaborator.Login),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/collaborators?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(
                    json: Page(Collaborator(login: "second", role: "read")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
                1 => Responses.Json(Page(Collaborator(login: "first", role: "maintain"))),
                _ => Unexpected(index),
            }
        );

        RepoCollaboratorsResponse response = await this.ListAsync(upstream);

        Assert.Equal(
            expected:
            [
                new RepoCollaborator(Login: "first", Role: "maintain"),
                new RepoCollaborator(Login: "second", Role: "read"),
            ],
            actual: response.Collaborators
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ABotLoginIsListedAsync()
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(Page(Collaborator("dependabot[bot]")));

        Assert.Equal(
            expected: [new RepoCollaborator(Login: "dependabot[bot]", Role: "write")],
            actual: response.Collaborators
        );
    }

    [Theory]
    [InlineData("""[{"login":"credfeto"}]""")]
    [InlineData("""[{"login":"credfeto","role_name":null}]""")]
    [InlineData("""[{"login":"credfeto","role_name":""}]""")]
    public async Task AMissingRoleIsADashAsync(string json)
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(json);

        Assert.Equal(expected: [new RepoCollaborator(Login: "credfeto", Role: "-")], actual: response.Collaborators);
    }

    [Fact]
    public async Task ControlCharactersInTheRoleAreSpacesAsync()
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(
            "[{\"login\":\"credfeto\",\"role_name\":\"custom\\nrole\\u0007x\\u2028y\"}]"
        );

        Assert.Equal(
            expected: [new RepoCollaborator(Login: "credfeto", Role: "custom role x y")],
            actual: response.Collaborators
        );
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        RepoCollaboratorsFailedException exception = await Assert.ThrowsAsync<RepoCollaboratorsFailedException>(() =>
            this.ListJsonAsync(Numbered(1001))
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        RepoCollaboratorsResponse response = await this.ListJsonAsync(Numbered(1000));

        Assert.Equal(expected: 1000, actual: response.Collaborators.Count);
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: NOT_FOUND, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Fact]
    public async Task ForbiddenIsAFailureNotAnEmptyListAsync()
    {
        RepoCollaboratorsFailedException exception = await this.FailsWithStatusAsync(HttpStatusCode.Forbidden);

        Assert.Equal(expected: FORBIDDEN, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.Forbidden, actual: inner.StatusCode);
    }

    [Fact]
    public async Task HttpFailureIsFailedAsync()
    {
        RepoCollaboratorsFailedException exception = await this.FailsWithStatusAsync(HttpStatusCode.BadGateway);

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"role_name":"admin"}]""")]
    [InlineData("""[{"login":null}]""")]
    [InlineData("""[{"login":""}]""")]
    [InlineData("""[{"login":"[bot]"}]""")]
    [InlineData("""[{"login":"bad name"}]""")]
    [InlineData("""[{"login":"user\n"}]""")]
    [InlineData("""[{"login":"credfeto"},{"login":"bad.name"}]""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        RepoCollaboratorsFailedException exception = await Assert.ThrowsAsync<RepoCollaboratorsFailedException>(() =>
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
