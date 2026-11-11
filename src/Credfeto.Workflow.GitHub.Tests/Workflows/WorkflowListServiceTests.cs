using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Credfeto.Workflow.GitHub.Workflows;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Workflows;

public sealed class WorkflowListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not list the workflows of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY = "credfeto/credfeto-workflow-mcp has more than 1000 workflows to list";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/actions/workflows?per_page=100"
    );

    private static string Item(
        string path = "\".github/workflows/build.yml\"",
        string name = "\"Build\"",
        string state = "\"active\""
    )
    {
        return "{\"id\":1,\"name\":" + name + ",\"path\":" + path + ",\"state\":" + state + "}";
    }

    private static string PathItem(string path)
    {
        return Item(path: "\"" + path + "\"");
    }

    private static string Page(params string[] items)
    {
        return "{\"total_count\":"
            + items.Length.ToString(CultureInfo.InvariantCulture)
            + ",\"workflows\":["
            + string.Join(separator: ',', value: items)
            + "]}";
    }

    private static string Numbered(int count)
    {
        return Page([
            .. Enumerable
                .Range(start: 1, count: count)
                .Select(number =>
                    PathItem(".github/workflows/w" + number.ToString(CultureInfo.InvariantCulture) + ".yml")
                ),
        ]);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private WorkflowListService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<WorkflowListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<WorkflowListResponse> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    private static IReadOnlyList<string> Paths(WorkflowListResponse response)
    {
        return [.. response.Workflows.Select(entry => entry.Path)];
    }

    [Fact]
    public async Task ReadsTheWorkflowsOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page(Item())));

        WorkflowListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(
            expected: [new WorkflowEntry(Name: "Build", Path: ".github/workflows/build.yml", State: "active")],
            actual: response.Workflows
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task NoWorkflowsIsEmptyAsync()
    {
        WorkflowListResponse response = await this.ListJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.Workflows);
    }

    [Fact]
    public async Task WorkflowsAreSortedByPathAsync()
    {
        WorkflowListResponse response = await this.ListJsonAsync(
            Page(
                PathItem(".github/workflows/z.yml"),
                PathItem(".github/workflows/B.yml"),
                PathItem(".github/workflows/a.yml")
            )
        );

        Assert.Equal(
            expected: [".github/workflows/B.yml", ".github/workflows/a.yml", ".github/workflows/z.yml"],
            actual: Paths(response)
        );
    }

    [Theory]
    [InlineData("active")]
    [InlineData("deleted")]
    [InlineData("disabled_fork")]
    [InlineData("disabled_inactivity")]
    [InlineData("disabled_manually")]
    public async Task EveryDocumentedStateIsListedAsync(string state)
    {
        WorkflowListResponse response = await this.ListJsonAsync(Page(Item(state: "\"" + state + "\"")));

        Assert.Equal(expected: state, actual: Assert.Single(response.Workflows).State);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task EmptyNameIsShownAsPlaceholderAsync(string name)
    {
        WorkflowListResponse response = await this.ListJsonAsync(Page(Item(name: name)));

        Assert.Equal(expected: "-", actual: Assert.Single(response.Workflows).Name);
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/actions/workflows?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(
                    json: Page(PathItem(".github/workflows/b.yml")),
                    nextLink: "<" + secondPage + ">; rel=\"next\""
                ),
                1 => Responses.Json(Page(PathItem(".github/workflows/a.yml"))),
                _ => Unexpected(index),
            }
        );

        WorkflowListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: [".github/workflows/a.yml", ".github/workflows/b.yml"], actual: Paths(response));
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        WorkflowListRefusedException exception = await Assert.ThrowsAsync<WorkflowListRefusedException>(() =>
            this.ListJsonAsync(Numbered(1001))
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        WorkflowListResponse response = await this.ListJsonAsync(Numbered(1000));

        Assert.Equal(expected: 1000, actual: response.Workflows.Count);
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
    public async Task HttpFailureIsFailedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        WorkflowListFailedException exception = await Assert.ThrowsAsync<WorkflowListFailedException>(() =>
            this.ListAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"workflows":[null]}""")]
    [InlineData("""{"workflows":[{"name":"Build","state":"active"}]}""")]
    [InlineData("""{"workflows":[{"name":"Build","path":" ","state":"active"}]}""")]
    [InlineData("""{"workflows":[{"name":"Build","path":"a.yml"}]}""")]
    [InlineData("""{"workflows":[{"name":"Build","path":"a.yml","state":null}]}""")]
    [InlineData("""{"workflows":[{"name":"Build","path":"a.yml","state":"enabled"}]}""")]
    [InlineData("""{"workflows":[{"name":"Build","path":"a.yml","state":"ACTIVE"}]}""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        WorkflowListFailedException exception = await Assert.ThrowsAsync<WorkflowListFailedException>(() =>
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
