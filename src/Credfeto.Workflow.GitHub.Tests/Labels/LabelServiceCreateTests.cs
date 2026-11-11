using System;
using System.Globalization;
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

public sealed class LabelServiceCreateTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string DESCRIPTION = "Generic bug fix";
    private const string COLOUR = "d73a4a";
    private const string FAILED = "could not create the label 'Bug' in credfeto/credfeto-workflow-mcp";

    private const string ALREADY_EXISTS = """
        {"message":"Validation Failed","errors":[{"resource":"Label","code":"already_exists","field":"name"}]}
        """;

    private const string CREATE_BODY = """{"name":"Bug","color":"d73a4a","description":"Generic bug fix"}""";

    private static readonly Uri LabelsUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels");
    private static readonly Uri LabelUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels/Bug");

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage AlreadyExists()
    {
        return Responses.StatusWithJson(statusCode: HttpStatusCode.UnprocessableEntity, json: ALREADY_EXISTS);
    }

    private Task<LabelCreateResponse> CreateAsync(
        RecordingHttpMessageHandler upstream,
        string name = "Bug",
        string description = DESCRIPTION,
        string colour = COLOUR,
        string repository = REPOSITORY
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );
        LabelService service = new(rest);

        return service
            .CreateAsync(
                repository: repository,
                name: name,
                description: description,
                colour: colour,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    private static void AssertRequest(RecordedRequest request, HttpMethod method, Uri uri, string? body)
    {
        Assert.Equal(expected: method, actual: request.Method);
        Assert.Equal(expected: uri, actual: request.RequestUri);
        Assert.Equal(expected: body, actual: request.Body);
    }

    private static void AssertResponse(LabelCreateResponse response, LabelCreateOutcome outcome)
    {
        Assert.Equal(
            expected: new LabelCreateResponse(
                Outcome: outcome,
                Repository: REPOSITORY,
                Name: "Bug",
                Colour: COLOUR,
                Description: DESCRIPTION
            ),
            actual: response
        );
    }

    [Fact]
    public async Task CreatesAMissingLabelAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.StatusWithJson(
                    statusCode: HttpStatusCode.Created,
                    json: """{"name":"Bug","color":"d73a4a","description":"Generic bug fix"}"""
                )
                : Unexpected(index)
        );

        LabelCreateResponse response = await this.CreateAsync(upstream);

        AssertResponse(response: response, outcome: LabelCreateOutcome.Created);
        RecordedRequest request = Assert.Single(upstream.Requests);
        AssertRequest(request: request, method: HttpMethod.Post, uri: LabelsUri, body: CREATE_BODY);
    }

    [Fact]
    public async Task NameAndColourAreNormalisedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"name":"Bug"}""")
        );

        LabelCreateResponse response = await this.CreateAsync(upstream: upstream, name: "BUG", colour: "D73A4A");

        AssertResponse(response: response, outcome: LabelCreateOutcome.Created);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: CREATE_BODY, actual: request.Body);
    }

    [Theory]
    [InlineData("""{"name":"Bug","color":"d73a4a","description":"Generic bug fix"}""")]
    [InlineData("""{"name":"Bug","color":"D73A4A","description":"Generic bug fix"}""")]
    public async Task IdenticalExistingLabelIsUnchangedAsync(string existing)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Json(existing),
                _ => Unexpected(index),
            }
        );

        LabelCreateResponse response = await this.CreateAsync(upstream);

        AssertResponse(response: response, outcome: LabelCreateOutcome.Unchanged);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Post, uri: LabelsUri, body: CREATE_BODY),
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: LabelUri, body: null)
        );
    }

    [Theory]
    [InlineData("""{"name":"bug","color":"d73a4a","description":"Generic bug fix"}""")]
    [InlineData("""{"name":"Bug","color":"ffffff","description":"Generic bug fix"}""")]
    [InlineData("""{"name":"Bug","color":"d73a4a","description":"Something else"}""")]
    [InlineData("""{"name":"Bug","color":"d73a4a","description":null}""")]
    [InlineData("""{"name":"Bug","color":"d73a4a"}""")]
    public async Task DifferentExistingLabelIsUpdatedAsync(string existing)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Json(existing),
                2 => Responses.Json("""{"name":"Bug","color":"d73a4a","description":"Generic bug fix"}"""),
                _ => Unexpected(index),
            }
        );

        LabelCreateResponse response = await this.CreateAsync(upstream);

        AssertResponse(response: response, outcome: LabelCreateOutcome.Updated);
        Assert.Collection(
            upstream.Requests,
            request => AssertRequest(request: request, method: HttpMethod.Post, uri: LabelsUri, body: CREATE_BODY),
            request => AssertRequest(request: request, method: HttpMethod.Get, uri: LabelUri, body: null),
            request =>
                AssertRequest(
                    request: request,
                    method: HttpMethod.Patch,
                    uri: LabelUri,
                    body: """{"new_name":"Bug","color":"d73a4a","description":"Generic bug fix"}"""
                )
        );
    }

    [Fact]
    public async Task LabelNameIsEscapedInItsUriAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Json("""{"name":"Tech Debt","color":"30027a","description":"Technical debt"}"""),
                _ => Unexpected(index),
            }
        );

        LabelCreateResponse response = await this.CreateAsync(
            upstream: upstream,
            name: "tech debt",
            description: "Technical debt",
            colour: "30027a"
        );

        Assert.Equal(expected: LabelCreateOutcome.Unchanged, actual: response.Outcome);
        Assert.Equal(expected: "Tech Debt", actual: response.Name);
        Assert.Equal(
            expected: new Uri(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels/Tech%20Debt"),
            actual: upstream.Requests[1].RequestUri
        );
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.NotFound));

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it",
            actual: exception.Message
        );
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: HttpStatusCode.NotFound, actual: inner.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{}")]
    [InlineData(
        HttpStatusCode.UnprocessableEntity,
        """{"message":"Validation Failed","errors":[{"code":"invalid"}]}"""
    )]
    [InlineData(HttpStatusCode.Forbidden, ALREADY_EXISTS)]
    public async Task CreateFailureFailsTheCreateAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.StatusWithJson(statusCode: status, json: json) : Unexpected(index)
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ReadFailureAfterConflictFailsTheCreateAsync(HttpStatusCode status)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Status(status),
                _ => Unexpected(index),
            }
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        HttpRequestException inner = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: status, actual: inner.StatusCode);
    }

    [Fact]
    public async Task UpdateFailureFailsTheCreateAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Json("""{"name":"Bug","color":"ffffff","description":"Old"}"""),
                2 => Responses.Status(HttpStatusCode.BadGateway),
                _ => Unexpected(index),
            }
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task InvalidCreateResponseFailsTheCreateAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: "null")
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task InvalidExistingLabelResponseFailsTheCreateAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => AlreadyExists(),
                1 => Responses.Json("null"),
                _ => Unexpected(index),
            }
        );

        LabelChangeFailedException exception = await Assert.ThrowsAsync<LabelChangeFailedException>(() =>
            this.CreateAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task InvalidRepositoryIsRejectedAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Bug,Enhancement")]
    [InlineData("Bug\n")]
    [InlineData("Not A Standard Label")]
    public async Task InvalidNameIsRejectedAsync(string name)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(upstream: upstream, name: name)
        );

        Assert.Equal(expected: "name", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Generic\nbug fix")]
    [InlineData(
        "12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901"
    )]
    public async Task InvalidDescriptionIsRejectedAsync(string description)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(upstream: upstream, description: description)
        );

        Assert.Equal(expected: "description", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#d73a4a")]
    [InlineData("d73a4")]
    [InlineData("d73a4a0")]
    [InlineData("d73a4g")]
    public async Task InvalidColourIsRejectedAsync(string colour)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(upstream: upstream, colour: colour)
        );

        Assert.Equal(expected: "color", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
