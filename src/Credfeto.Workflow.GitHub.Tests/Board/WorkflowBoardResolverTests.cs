using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Board;

public sealed class WorkflowBoardResolverTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string COULD_NOT_READ = "could not read the projects of " + REPOSITORY;
    private const string WORKFLOW_NODE =
        """{"id":"PVT_kwDOA","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}""";

    private static readonly WorkflowProject ExpectedProject = new(
        ProjectNodeId: "PVT_kwDOA",
        Number: 7,
        Owner: "credfeto"
    );

    private readonly RepositoryProjectCache _cache = new(
        new HttpContextAccessor { HttpContext = CallerContext.WithBearerToken() }
    );

    [Fact]
    public async Task SingleOpenWorkflowBoardIsResolvedAndCachedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                nodes: WORKFLOW_NODE
                    + ","
                    + Node(
                        id: "PVT_other",
                        number: 8,
                        title: "Roadmap",
                        closed: "false",
                        resourcePath: "/users/credfeto/projects/8"
                    )
            )
        );

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(expected: ExpectedProject, actual: project);
        Assert.Equal(expected: ExpectedProject, actual: this._cache.GetProject(REPOSITORY));

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "credfeto", actual: variables.GetProperty("owner").GetString());
        Assert.Equal(expected: "credfeto-workflow-mcp", actual: variables.GetProperty("name").GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("after").ValueKind);
        Assert.Contains(
            expectedSubstring: "projectsV2(first: 100, after:$after)",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CachedBoardIsReturnedWithoutQueryingAsync()
    {
        this._cache.SetProject(repository: REPOSITORY, project: ExpectedProject);

        using RecordingHttpMessageHandler upstream = new(static _ => Page(nodes: WORKFLOW_NODE));

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(expected: ExpectedProject, actual: project);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task SecondResolveUsesTheCacheAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page(nodes: WORKFLOW_NODE));
        WorkflowBoardResolver resolver = this.CreateResolver(upstream);

        WorkflowProject first = await resolver.ResolveAsync(
            repository: REPOSITORY,
            cancellationToken: this.CancellationToken()
        );
        WorkflowProject second = await resolver.ResolveAsync(
            repository: REPOSITORY,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: first, actual: second);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task InvalidateForcesAFreshQueryAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page(nodes: WORKFLOW_NODE));
        WorkflowBoardResolver resolver = this.CreateResolver(upstream);

        _ = await resolver.ResolveAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());
        resolver.Invalidate(REPOSITORY);

        Assert.Null(this._cache.GetProject(REPOSITORY));

        _ = await resolver.ResolveAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task FailureIsNotCachedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0 ? Responses.Status(HttpStatusCode.BadGateway) : Page(nodes: WORKFLOW_NODE)
        );
        WorkflowBoardResolver resolver = this.CreateResolver(upstream);

        _ = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            resolver.ResolveAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Null(this._cache.GetProject(REPOSITORY));

        WorkflowProject project = await resolver.ResolveAsync(
            repository: REPOSITORY,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: ExpectedProject, actual: project);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(
        """{"id":"PVT_closed","number":3,"title":"Workflow","closed":true,"resourcePath":"/users/credfeto/projects/3"}"""
    )]
    [InlineData(
        """{"id":"PVT_lower","number":4,"title":"workflow","closed":false,"resourcePath":"/users/credfeto/projects/4"}"""
    )]
    [InlineData(
        """{"id":"PVT_space","number":5,"title":"Workflow ","closed":false,"resourcePath":"/users/credfeto/projects/5"}"""
    )]
    [InlineData("""{"id":"PVT_none","number":6,"closed":false,"resourcePath":"/users/credfeto/projects/6"}""")]
    [InlineData("null")]
    public async Task NoOpenWorkflowBoardIsNotFoundAsync(string nodes)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: nodes));

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "no project titled Workflow is linked to " + REPOSITORY, actual: exception.Message);
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task NullNodesListIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json(
                """{"data":{"repository":{"projectsV2":{"nodes":null,"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}"""
            )
        );

        _ = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() => this.ResolveAsync(upstream).AsTask());
    }

    [Fact]
    public async Task ClosedFlagAbsentIsTreatedAsOpenAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                nodes: """{"id":"PVT_kwDOA","number":7,"title":"Workflow","resourcePath":"/users/credfeto/projects/7"}"""
            )
        );

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(expected: ExpectedProject, actual: project);
    }

    [Fact]
    public async Task ClosedWorkflowBoardIsIgnoredBesideAnOpenOneAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                nodes: Node(
                    id: "PVT_closed",
                    number: 3,
                    title: "Workflow",
                    closed: "true",
                    resourcePath: "/users/credfeto/projects/3"
                )
                    + ","
                    + WORKFLOW_NODE
            )
        );

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(expected: ExpectedProject, actual: project);
    }

    [Fact]
    public async Task TwoOpenWorkflowBoardsAreAmbiguousAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                nodes: WORKFLOW_NODE
                    + ","
                    + Node(
                        id: "PVT_second",
                        number: 9,
                        title: "Workflow",
                        closed: "null",
                        resourcePath: "/orgs/credfeto/projects/9"
                    )
            )
        );

        WorkflowBoardAmbiguousException exception = await Assert.ThrowsAsync<WorkflowBoardAmbiguousException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(
            expected: "more than one project titled Workflow is linked to " + REPOSITORY,
            actual: exception.Message
        );
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task OwnerIsTakenFromTheProjectPathAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(
                nodes: Node(
                    id: "PVT_org",
                    number: 0,
                    title: "Workflow",
                    closed: "false",
                    resourcePath: "/orgs/funfair-tech/projects/0"
                )
            )
        );

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(
            expected: new WorkflowProject(ProjectNodeId: "PVT_org", Number: 0, Owner: "funfair-tech"),
            actual: project
        );
    }

    [Theory]
    [InlineData("""{"number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}""")]
    [InlineData(
        """{"id":"PVT bad","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}"""
    )]
    [InlineData(
        """{"id":"","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}"""
    )]
    [InlineData(
        """{"id":"PVT_kwDOA\n","number":7,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}"""
    )]
    [InlineData("""{"id":"PVT_kwDOA","title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}""")]
    [InlineData(
        """{"id":"PVT_kwDOA","number":-1,"title":"Workflow","closed":false,"resourcePath":"/users/credfeto/projects/7"}"""
    )]
    public async Task InvalidProjectIdOrNumberIsRejectedAsync(string node)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Page(nodes: node));

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "unexpected project id/number for " + REPOSITORY, actual: exception.Message);
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"/users/credfeto/projects/7\\n\"")]
    [InlineData("\"/teams/credfeto/projects/7\"")]
    [InlineData("\"/users/cred feto/projects/7\"")]
    [InlineData("\"/users/credfeto/projects/x\"")]
    [InlineData("\"/users/credfeto/projects/\"")]
    [InlineData("\"https://github.com/users/credfeto/projects/7\"")]
    [InlineData("\"/users//projects/7\"")]
    public async Task InvalidProjectPathIsRejectedAsync(string resourcePath)
    {
        using RecordingHttpMessageHandler upstream = new(_ =>
            Page(
                nodes: """{"id":"PVT_kwDOA","number":7,"title":"Workflow","closed":false,"resourcePath":"""
                    + resourcePath
                    + "}"
            )
        );

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: "unexpected project path for " + REPOSITORY, actual: exception.Message);
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task BoardOnALaterPageIsFoundUsingTheCursorAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Page(
                    nodes: "null,"
                        + Node(
                            id: "PVT_other",
                            number: 8,
                            title: "Roadmap",
                            closed: "false",
                            resourcePath: "/users/credfeto/projects/8"
                        ),
                    hasNextPage: true,
                    endCursor: "cursor-1"
                )
                : Page(nodes: WORKFLOW_NODE)
        );

        WorkflowProject project = await this.ResolveAsync(upstream);

        Assert.Equal(expected: ExpectedProject, actual: project);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument first = JsonDocument.Parse(upstream.Requests[0].Body ?? string.Empty);
        using JsonDocument second = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        Assert.Equal(
            expected: JsonValueKind.Null,
            actual: first.RootElement.GetProperty("variables").GetProperty("after").ValueKind
        );
        Assert.Equal(
            expected: "cursor-1",
            actual: second.RootElement.GetProperty("variables").GetProperty("after").GetString()
        );
    }

    [Fact]
    public async Task MatchesAcrossPagesAreAmbiguousAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index == 0
                ? Page(nodes: WORKFLOW_NODE, hasNextPage: true, endCursor: "cursor-1")
                : Page(nodes: WORKFLOW_NODE)
        );

        _ = await Assert.ThrowsAsync<WorkflowBoardAmbiguousException>(() => this.ResolveAsync(upstream).AsTask());
    }

    [Theory]
    [InlineData("""{"data":{"repository":null}}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"repository":{"projectsV2":null}}}""")]
    [InlineData("""{"data":{"repository":{}}}""")]
    [InlineData("""{"data":{"repository":{"projectsV2":{"nodes":[],"pageInfo":null}}}}""")]
    [InlineData("""{"data":{"repository":{"projectsV2":{"nodes":[]}}}}""")]
    [InlineData(
        """{"data":{"repository":{"projectsV2":{"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":null}}}}}"""
    )]
    [InlineData("""{"data":{"repository":{"projectsV2":{"nodes":[],"pageInfo":{"hasNextPage":true}}}}}""")]
    public async Task IncompleteResponseCouldNotBeReadAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task RepeatedCursorCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Page(nodes: string.Empty, hasNextPage: true, endCursor: "same-cursor")
        );

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task EndlessPagingIsCappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            Page(
                nodes: string.Empty,
                hasNextPage: true,
                endCursor: "cursor-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
        );

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        Assert.Equal(expected: 100, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task GraphQlErrorsCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"Could not resolve to a Repository"}],"data":null}""")
        );

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task UnreadableBodyCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureCouldNotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        WorkflowBoardInvalidException exception = await Assert.ThrowsAsync<WorkflowBoardInvalidException>(() =>
            this.ResolveAsync(upstream).AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task CancellationIsNotWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page(nodes: WORKFLOW_NODE));
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            this.CreateResolver(upstream)
                .ResolveAsync(repository: REPOSITORY, cancellationToken: cancelled.Token)
                .AsTask()
        );

        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("/credfeto-workflow-mcp")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Page(nodes: WORKFLOW_NODE));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateResolver(upstream)
                .ResolveAsync(repository: repository, cancellationToken: this.CancellationToken())
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    private static string Node(string id, int number, string title, string closed, string resourcePath)
    {
        return "{\"id\":\""
            + id
            + "\",\"number\":"
            + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ",\"title\":\""
            + title
            + "\",\"closed\":"
            + closed
            + ",\"resourcePath\":\""
            + resourcePath
            + "\"}";
    }

    private static HttpResponseMessage Page(string nodes, bool hasNextPage = false, string? endCursor = null)
    {
        string cursor = endCursor is null ? "null" : "\"" + endCursor + "\"";

        return Responses.Json(
            "{\"data\":{\"repository\":{\"projectsV2\":{\"nodes\":["
                + nodes
                + "],\"pageInfo\":{\"hasNextPage\":"
                + (hasNextPage ? "true" : "false")
                + ",\"endCursor\":"
                + cursor
                + "}}}}}"
        );
    }

    private WorkflowBoardResolver CreateResolver(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client, cache: this._cache);
    }

    private ValueTask<WorkflowProject> ResolveAsync(HttpMessageHandler upstream)
    {
        return this.CreateResolver(upstream)
            .ResolveAsync(repository: REPOSITORY, cancellationToken: this.CancellationToken());
    }
}
