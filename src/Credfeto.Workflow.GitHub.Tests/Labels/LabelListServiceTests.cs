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
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Labels;

public sealed class LabelListServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string NOT_FOUND =
        "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it";
    private const string FAILED = "could not list the labels of credfeto/credfeto-workflow-mcp";
    private const string TOO_MANY = "credfeto/credfeto-workflow-mcp has more than 1000 labels to list";

    private static readonly Uri ListUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels?per_page=100"
    );

    private static string Label(string name)
    {
        return "{\"id\":1,\"name\":\"" + name + "\",\"color\":\"ededed\",\"description\":null}";
    }

    private static string Page(params string[] names)
    {
        return "[" + string.Join(separator: ',', values: names.Select(Label)) + "]";
    }

    private static string Numbered(int count)
    {
        return Page([
            .. Enumerable
                .Range(start: 1, count: count)
                .Select(number => "label-" + number.ToString(CultureInfo.InvariantCulture)),
        ]);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private LabelListService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(rest);
    }

    private async Task<LabelListResponse> ListAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY
    )
    {
        return await this.CreateService(upstream)
            .ListAsync(repository: repository, cancellationToken: this.CancellationToken());
    }

    private async Task<LabelListResponse> ListJsonAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        return await this.ListAsync(upstream);
    }

    [Fact]
    public async Task ReadsTheLabelsOfTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(Page("bug", "Priority 1")));

        LabelListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Equal(expected: ["bug", "Priority 1"], actual: response.Labels);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: ListUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task NoLabelsIsEmptyAsync()
    {
        LabelListResponse response = await this.ListJsonAsync(Page());

        Assert.Equal(expected: REPOSITORY, actual: response.Repository);
        Assert.Empty(response.Labels);
    }

    [Fact]
    public async Task LabelsKeepGitHubsOrderAsync()
    {
        LabelListResponse response = await this.ListJsonAsync(Page("zebra", "Alpha", "middle"));

        Assert.Equal(expected: ["zebra", "Alpha", "middle"], actual: response.Labels);
    }

    [Fact]
    public async Task EveryPageIsReadAsync()
    {
        const string secondPage = GitHubServices.ApiBaseUrl + "repositories/1/labels?per_page=100&page=2";
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(json: Page("first"), nextLink: "<" + secondPage + ">; rel=\"next\""),
                1 => Responses.Json(Page("second")),
                _ => Unexpected(index),
            }
        );

        LabelListResponse response = await this.ListAsync(upstream);

        Assert.Equal(expected: ["first", "second"], actual: response.Labels);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task MoreThanTheCapIsRefusedAsync()
    {
        LabelListFailedException exception = await Assert.ThrowsAsync<LabelListFailedException>(() =>
            this.ListJsonAsync(Numbered(1001))
        );

        Assert.Equal(expected: TOO_MANY, actual: exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ExactlyTheCapIsListedAsync()
    {
        LabelListResponse response = await this.ListJsonAsync(Numbered(1000));

        Assert.Equal(expected: 1000, actual: response.Labels.Count);
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

        LabelListFailedException exception = await Assert.ThrowsAsync<LabelListFailedException>(() =>
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
    [InlineData("""[{"color":"ededed"}]""")]
    [InlineData("""[{"name":null}]""")]
    [InlineData("""[{"name":""}]""")]
    [InlineData("""[{"name":" "}]""")]
    public async Task InvalidResponseIsFailedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            json[0] == '<' ? Responses.Html(json) : Responses.Json(json)
        );

        LabelListFailedException exception = await Assert.ThrowsAsync<LabelListFailedException>(() =>
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
