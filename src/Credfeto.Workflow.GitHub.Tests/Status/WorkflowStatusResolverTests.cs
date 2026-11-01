using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Models;
using Credfeto.Workflow.GitHub.Status;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Status;

public sealed class WorkflowStatusResolverTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string COULD_NOT_READ = "could not read the status fields of project 7";

    private const string STATUS_FIELD =
        """{"id":"PVTSSF_status","name":"Status","options":[{"id":"opt_todo","name":"Todo"},{"id":"opt_dev","name":"Development"},{"id":"opt_review","name":"AI Review"}]}""";

    private const string LEGACY_FIELD =
        """{"id":"PVTSSF_legacy","name":"Workflow Status","options":[{"id":"legacy_dev","name":"Development"},{"id":"legacy_done","name":"Complete"}]}""";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_kwDOA", Number: 7, Owner: "credfeto");

    private readonly RepositoryProjectCache _cache = new(
        new HttpContextAccessor { HttpContext = CallerContext.WithBearerToken() }
    );

    [Fact]
    public async Task StateIsMatchedCaseInsensitivelyToTheCanonicalOptionAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "ai REVIEW");

        Assert.Equal(
            expected: new ResolvedStatusOption(FieldId: "PVTSSF_status", FieldName: "Status", OptionId: "opt_review"),
            actual: option
        );
    }

    [Fact]
    public async Task RequestCarriesTheProjectNodeIdAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        _ = await this.ResolveAsync(upstream: upstream, state: "Development");

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "PVT_kwDOA", actual: variables.GetProperty("projectId").GetString());
        Assert.Contains(
            expectedSubstring: "ProjectV2SingleSelectField",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task LegacyFieldWinsOverStatusFieldAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD + "," + LEGACY_FIELD));

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "development");

        Assert.Equal(
            expected: new ResolvedStatusOption(
                FieldId: "PVTSSF_legacy",
                FieldName: "Workflow Status",
                OptionId: "legacy_dev"
            ),
            actual: option
        );
        Assert.Equal(expected: "PVTSSF_legacy", actual: this._cache.GetStatusField(REPOSITORY)?.FieldId);
    }

    [Fact]
    public async Task StatusFieldIsUsedWhenLegacyFieldIsAbsentAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Fields("{}," + """{"id":"PVTSSF_other","name":"Priority","options":[]},""" + STATUS_FIELD)
        );

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "Development");

        Assert.Equal(
            expected: new ResolvedStatusOption(FieldId: "PVTSSF_status", FieldName: "Status", OptionId: "opt_dev"),
            actual: option
        );
    }

    [Fact]
    public async Task ResolvedFieldIsCachedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        _ = await this.ResolveAsync(upstream: upstream, state: "Development");

        WorkflowStatusField? cached = this._cache.GetStatusField(REPOSITORY);
        Assert.NotNull(cached);
        Assert.Equal(expected: "PVTSSF_status", actual: cached.FieldId);
        Assert.Equal(expected: "Status", actual: cached.FieldName);
        Assert.Equal(expected: 3, actual: cached.OptionIds.Count);
    }

    [Fact]
    public async Task CachedFieldIsUsedWithoutARequestAsync()
    {
        this._cache.SetStatusField(
            repository: REPOSITORY,
            statusField: new(
                FieldId: "PVTSSF_cached",
                FieldName: "Workflow Status",
                OptionIds: new Dictionary<string, string>(StringComparer.Ordinal) { ["Complete"] = "cached_done" }
            )
        );
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "COMPLETE");

        Assert.Equal(
            expected: new ResolvedStatusOption(
                FieldId: "PVTSSF_cached",
                FieldName: "Workflow Status",
                OptionId: "cached_done"
            ),
            actual: option
        );
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task StaleCachedFieldIsInvalidatedAndReadAgainOnceAsync()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this._cache.SetStatusField(
            repository: REPOSITORY,
            statusField: new(
                FieldId: "PVTSSF_stale",
                FieldName: "Status",
                OptionIds: new Dictionary<string, string>(StringComparer.Ordinal) { ["Todo"] = "stale_todo" }
            )
        );
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "AI Review");

        Assert.Equal(
            expected: new ResolvedStatusOption(FieldId: "PVTSSF_status", FieldName: "Status", OptionId: "opt_review"),
            actual: option
        );
        _ = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "PVTSSF_status", actual: this._cache.GetStatusField(REPOSITORY)?.FieldId);
        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task StaleCachedFieldStillMissingTheOptionFailsAfterOneReadAsync()
    {
        this._cache.SetStatusField(
            repository: REPOSITORY,
            statusField: new(
                FieldId: "PVTSSF_status",
                FieldName: "Status",
                OptionIds: new Dictionary<string, string>(StringComparer.Ordinal) { ["Todo"] = "opt_todo" }
            )
        );
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        _ = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Complete").AsTask()
        );

        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task MissingOptionIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        StatusOptionMissingException exception = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.ResolveAsync(upstream: upstream, state: "human review").AsTask()
        );

        Assert.Equal(expected: "project 7 has no 'Human Review' option on its Status field", actual: exception.Message);
        Assert.Equal(expected: "PVTSSF_status", actual: this._cache.GetStatusField(REPOSITORY)?.FieldId);
    }

    [Fact]
    public async Task MissingStatusFieldIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Fields("""{"id":"PVTSSF_other","name":"Priority","options":[]}""")
        );

        WorkflowStatusFieldNotFoundException exception = await Assert.ThrowsAsync<WorkflowStatusFieldNotFoundException>(
            () =>
                this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        Assert.Equal(expected: "project 7 has no Status or Workflow Status field", actual: exception.Message);
        Assert.Null(this._cache.GetStatusField(REPOSITORY));
    }

    [Fact]
    public async Task FieldNamesAreMatchedExactlyAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Fields("""{"id":"PVTSSF_lower","name":"status","options":[{"id":"opt_dev","name":"Development"}]}""")
        );

        _ = await Assert.ThrowsAsync<WorkflowStatusFieldNotFoundException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );
    }

    [Fact]
    public async Task UnusableFieldsAndOptionsAreSkippedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Fields(
                "null,{},"
                    + """{"id":"","name":"Status","options":[]},"""
                    + """{"id":"PVTSSF_nameless","name":"","options":[]},"""
                    + """{"id":"PVTSSF_status","name":"Status","options":[null,{},{"id":"","name":"Development"},{"id":"opt_nameless","name":""},{"id":"opt_dev","name":"Development"},{"id":"opt_dup","name":"Development"}]}"""
            )
        );

        ResolvedStatusOption option = await this.ResolveAsync(upstream: upstream, state: "Development");

        Assert.Equal(
            expected: new ResolvedStatusOption(FieldId: "PVTSSF_status", FieldName: "Status", OptionId: "opt_dev"),
            actual: option
        );

        WorkflowStatusField? cached = this._cache.GetStatusField(REPOSITORY);
        Assert.NotNull(cached);
        Assert.Equal(expected: "opt_dev", actual: Assert.Single(cached.OptionIds).Value);
    }

    [Fact]
    public async Task FieldWithNoOptionsHasNoMatchAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Fields("""{"id":"PVTSSF_status","name":"Status","options":null}""")
        );

        _ = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        WorkflowStatusField? cached = this._cache.GetStatusField(REPOSITORY);
        Assert.NotNull(cached);
        Assert.Empty(cached.OptionIds);
    }

    [Theory]
    [InlineData("""{"data":{"node":null}}""")]
    [InlineData("""{"data":{"node":{}}}""")]
    [InlineData("""{"data":{"node":{"fields":null}}}""")]
    [InlineData("""{"data":{"node":{"fields":{"nodes":null}}}}""")]
    public async Task MissingFieldsAreWrappedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(json));

        StatusFieldReadFailedException exception = await Assert.ThrowsAsync<StatusFieldReadFailedException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
        Assert.Null(this._cache.GetStatusField(REPOSITORY));
    }

    [Fact]
    public async Task GraphQlErrorIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ =>
            Responses.Json("""{"errors":[{"message":"x"}],"data":null}""")
        );

        StatusFieldReadFailedException exception = await Assert.ThrowsAsync<StatusFieldReadFailedException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubGraphQlException>(exception.InnerException);
    }

    [Fact]
    public async Task InvalidResponseIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Html("<html></html>"));

        StatusFieldReadFailedException exception = await Assert.ThrowsAsync<StatusFieldReadFailedException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Fact]
    public async Task HttpFailureIsWrappedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.BadGateway));

        StatusFieldReadFailedException exception = await Assert.ThrowsAsync<StatusFieldReadFailedException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Development").AsTask()
        );

        Assert.Equal(expected: COULD_NOT_READ, actual: exception.Message);
        _ = Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task UnknownStateIsRejectedBeforeAnyRequestAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        _ = await Assert.ThrowsAsync<UnknownWorkflowStateException>(() =>
            this.ResolveAsync(upstream: upstream, state: "Todo").AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/credfeto-workflow-mcp/extra")]
    public async Task MalformedRepositoryIsRejectedBeforeAnyRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Fields(STATUS_FIELD));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateResolver(upstream)
                .ResolveAsync(
                    repository: repository,
                    project: Project,
                    state: "Development",
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    private static HttpResponseMessage Fields(string nodes)
    {
        return Responses.Json("{\"data\":{\"node\":{\"fields\":{\"nodes\":[" + nodes + "]}}}}");
    }

    private WorkflowStatusResolver CreateResolver(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client, cache: this._cache);
    }

    private ValueTask<ResolvedStatusOption> ResolveAsync(HttpMessageHandler upstream, string state)
    {
        return this.CreateResolver(upstream)
            .ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: state,
                cancellationToken: this.CancellationToken()
            );
    }
}
