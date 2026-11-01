using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

public sealed class RepositoryVisibilityLookupTests : GitHubClientTestBase
{
    private const string REPOSITORY = "secretorg/hidden-repo";
    private const string COULD_NOT_READ = "the visibility of a referenced repository could not be read";

    [Theory]
    [InlineData("PUBLIC", RepositoryVisibility.PUBLIC)]
    [InlineData("PRIVATE", RepositoryVisibility.PRIVATE)]
    [InlineData("INTERNAL", RepositoryVisibility.INTERNAL)]
    [InlineData("SOMETHING_NEW", RepositoryVisibility.PRIVATE)]
    public async Task VisibilityIsReadAsync(string visibility, RepositoryVisibility expected)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Responses.Json("{\"data\":{\"repository\":{\"visibility\":\"" + visibility + "\"}}}")
        );

        RepositoryVisibility actual = await this.LookupAsync(upstream);

        Assert.Equal(expected: expected, actual: actual);
    }

    [Fact]
    public async Task QueryNamesTheOwnerAndRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repository":{"visibility":"PUBLIC"}}}""")
        );

        _ = await this.LookupAsync(upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "secretorg", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "hidden-repo", actual: variables.GetProperty("r").GetString());
        Assert.Contains(
            expectedSubstring: "repository(owner:$o,name:$r){visibility}",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task MissingRepositoryIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repository":null}}""")
        );

        RepositoryVisibility actual = await this.LookupAsync(upstream);

        Assert.Equal(expected: RepositoryVisibility.NOT_FOUND, actual: actual);
    }

    [Fact]
    public async Task UnresolvableRepositoryErrorIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"errors":[{"message":"Could not resolve to a Repository with the name 'secretorg/hidden-repo'."}],"data":null}"""
            )
        );

        RepositoryVisibility actual = await this.LookupAsync(upstream);

        Assert.Equal(expected: RepositoryVisibility.NOT_FOUND, actual: actual);
    }

    [Fact]
    public async Task OtherGraphQlErrorFailsWithoutNamingTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Something broke for secretorg/hidden-repo"}],"data":null}""")
        );

        RepositoryVisibilityLookupFailedException exception = await this.AssertFailsAsync(upstream);

        Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task InvalidResponseFailsWithoutNamingTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Html("<html>secretorg/hidden-repo</html>")
        );

        RepositoryVisibilityLookupFailedException exception = await this.AssertFailsAsync(upstream);

        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureFailsWithoutNamingTheRepositoryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        RepositoryVisibilityLookupFailedException exception = await this.AssertFailsAsync(upstream);

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("secretorg")]
    [InlineData("secretorg/hidden/extra")]
    [InlineData("secretorg/hid den")]
    public async Task InvalidRepositoryIsRejectedWithoutQueryingAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"data":{"repository":{"visibility":"PUBLIC"}}}""")
        );

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateLookup(upstream)
                .GetVisibilityAsync(repository: repository, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    private async Task<RepositoryVisibilityLookupFailedException> AssertFailsAsync(HttpMessageHandler upstream)
    {
        RepositoryVisibilityLookupFailedException exception =
            await Assert.ThrowsAsync<RepositoryVisibilityLookupFailedException>(() =>
                this.LookupAsync(upstream).AsTask()
            );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.DoesNotContain(
            expectedSubstring: "hidden-repo",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );

        return exception;
    }

    private RepositoryVisibilityLookup CreateLookup(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client);
    }

    private ValueTask<RepositoryVisibility> LookupAsync(HttpMessageHandler upstream)
    {
        return this.CreateLookup(upstream)
            .GetVisibilityAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());
    }
}
