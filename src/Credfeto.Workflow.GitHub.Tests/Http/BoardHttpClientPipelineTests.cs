using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Models;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Http;

public sealed class BoardHttpClientPipelineTests : TestBase
{
    private const string BOARD_TOKEN = "github_pat_board_token_value";

    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const string PROJECTS =
        """{"data":{"repository":{"projectsV2":{"nodes":[{"id":"PVT_kwDOA","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}""";

    private const string STATUS_FIELDS =
        """{"data":{"node":{"fields":{"nodes":[{"id":"PVTSSF_status","name":"Status","options":[{"id":"opt_not_started","name":"Not Started"}]}]}}}}""";

    private const string ADDED = """{"data":{"addProjectV2ItemById":{"item":{"id":"PVTI_lADOA"}}}}""";

    private const string UPDATED =
        """{"data":{"updateProjectV2ItemFieldValue":{"projectV2Item":{"id":"PVTI_lADOA"}}}}""";

    private static readonly Uri UserUri = new(uriString: "user", uriKind: UriKind.Relative);

    private static readonly Uri IssueUrl = new("https://github.com/" + REPOSITORY + "/issues/42");

    [Fact]
    public async Task BoardClientSendsTheBoardTokenAndNeverTheCallersAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: BOARD_TOKEN);

        using HttpClient client = CreateClient(serviceProvider: serviceProvider, name: GitHubSetup.BoardHttpClientName);
        using HttpResponseMessage response = await client.GetAsync(
            requestUri: UserUri,
            cancellationToken: this.CancellationToken()
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + BOARD_TOKEN, actual: request.Authorization);
        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl + "user"), actual: request.RequestUri);
    }

    [Fact]
    public async Task CallerClientNeverSendsTheBoardTokenAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: BOARD_TOKEN);

        using HttpClient client = CreateClient(serviceProvider: serviceProvider, name: GitHubSetup.HttpClientName);
        using HttpResponseMessage response = await client.GetAsync(
            requestUri: UserUri,
            cancellationToken: this.CancellationToken()
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + CallerContext.Token, actual: request.Authorization);
    }

    [Fact]
    public async Task CallerServicesNeverSendTheBoardTokenAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Json("""{"id":1,"name":"octocat"}"""));
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: BOARD_TOKEN);

        TestItem item = await serviceProvider
            .GetRequiredService<IGitHubRestClient>()
            .GetAsync(
                relativeUri: "user",
                responseTypeInfo: TestJsonContext.Default.TestItem,
                cancellationToken: this.CancellationToken()
            );

        Assert.Equal(expected: "octocat", actual: item.Name);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + CallerContext.Token, actual: request.Authorization);
    }

    [Fact]
    public async Task BoardClientWithoutATokenSendsNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: null);

        using HttpClient client = CreateClient(serviceProvider: serviceProvider, name: GitHubSetup.BoardHttpClientName);

        _ = await Assert.ThrowsAsync<BoardTokenMissingException>(() =>
            client.GetAsync(requestUri: UserUri, cancellationToken: this.CancellationToken())
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Custom")]
    public static async Task BoardClientLoggedHeaderValuesAreAllRedactedAsync(string header)
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(boardToken: BOARD_TOKEN);

        HttpClientFactoryOptions options = serviceProvider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(GitHubSetup.BoardHttpClientName);

        Assert.True(condition: options.ShouldRedactHeaderValue(header), userMessage: "Every header must be redacted");
    }

    [Fact]
    public async Task BoardClientIsConfiguredForGitHubAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build(boardToken: BOARD_TOKEN);

        using HttpClient client = CreateClient(serviceProvider: serviceProvider, name: GitHubSetup.BoardHttpClientName);

        Assert.Equal(expected: new Uri(GitHubServices.ApiBaseUrl), actual: client.BaseAddress);
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task FallbackWriterSendsOnlyTheBoardTokenAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index => Success(index));
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: BOARD_TOKEN);

        IBoardFallbackWriter writer = serviceProvider.GetRequiredService<IBoardFallbackWriter>();

        Assert.True(condition: writer.IsConfigured, userMessage: "The board token is configured");

        await writer.AddAsNotStartedAsync(
            repository: REPOSITORY,
            contentNodeId: "I_kwDOABCDEF",
            url: IssueUrl,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.All(collection: upstream.Requests, action: static request =>
            Assert.Equal(expected: "Bearer " + BOARD_TOKEN, actual: request.Authorization)
        );
    }

    [Fact]
    public async Task FallbackWriterFailureNeverRevealsTheBoardTokenAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"errors":[{"type":"FORBIDDEN","message":"Resource not accessible by integration"}],"data":null}"""
            )
        );
        await using ServiceProvider serviceProvider = BuildWithCaller(upstream: upstream, boardToken: BOARD_TOKEN);

        IBoardFallbackWriter writer = serviceProvider.GetRequiredService<IBoardFallbackWriter>();

        BoardWriteException exception = await Assert.ThrowsAsync<BoardWriteException>(() =>
            writer
                .AddAsNotStartedAsync(
                    repository: REPOSITORY,
                    contentNodeId: "I_kwDOABCDEF",
                    url: IssueUrl,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(
                expectedSubstring: BOARD_TOKEN,
                actualString: current.ToString(),
                comparisonType: StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public async Task FallbackWriterWithoutATokenIsNotConfiguredAsync()
    {
        await using ServiceProvider serviceProvider = GitHubServices.Build();

        IBoardFallbackWriter writer = serviceProvider.GetRequiredService<IBoardFallbackWriter>();

        Assert.False(condition: writer.IsConfigured, userMessage: "No board token is configured");
    }

    private static HttpResponseMessage Success(int index)
    {
        return index switch
        {
            0 => Responses.Json(PROJECTS),
            1 => Responses.Json(STATUS_FIELDS),
            2 => Responses.Json(ADDED),
            _ => Responses.Json(UPDATED),
        };
    }

    private static ServiceProvider BuildWithCaller(HttpMessageHandler upstream, string? boardToken)
    {
        ServiceProvider serviceProvider = GitHubServices.Build(primaryHandler: upstream, boardToken: boardToken);
        serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = CallerContext.WithBearerToken();

        return serviceProvider;
    }

    private static HttpClient CreateClient(ServiceProvider serviceProvider, string name)
    {
        return serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
    }
}
