using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Models;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Cache;

public sealed class RepositoryProjectCacheTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string OTHER_REPOSITORY = "credfeto/other";
    private const string OTHER_CALLER_TOKEN = "other-caller-token-value";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_project", Number: 7, Owner: "credfeto");

    private static readonly string[] StatusOptionLookups = ["In Progress", "in progress", "Done", "DONE", "Backlog"];

    private readonly HttpContextAccessor _httpContextAccessor = new() { HttpContext = CallerContext.WithBearerToken() };

    private readonly RepositoryProjectCache _cache;

    public RepositoryProjectCacheTests()
    {
        this._cache = new(this._httpContextAccessor);
    }

    [Fact]
    public void UnknownRepositoryHasNothingCached()
    {
        Assert.Null(this._cache.GetProject(REPOSITORY));
        Assert.Null(this._cache.GetStatusField(REPOSITORY));
        Assert.Null(this._cache.GetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1));
    }

    [Fact]
    public void ProjectIsCachedCaseInsensitively()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);

        Assert.Equal(expected: Project, actual: this._cache.GetProject("Credfeto/Credfeto-Workflow-MCP"));
        Assert.Null(this._cache.GetProject(OTHER_REPOSITORY));
    }

    [Fact]
    public void CachedStatusFieldKeepsOptionsDifferingOnlyByCaseAndResolvesLikeTheOriginal()
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal)
        {
            ["In Progress"] = "OPT_progress",
            ["Done"] = "OPT_done",
            ["DONE"] = "OPT_done_upper",
        };
        WorkflowStatusField original = new(FieldId: "PVTSSF_status", FieldName: "Status", OptionIds: options);

        this._cache.SetStatusField(repository: REPOSITORY, statusField: original);

        WorkflowStatusField cached = AssertReallyNotNull(this._cache.GetStatusField(REPOSITORY));
        Assert.Equal(expected: "PVTSSF_status", actual: cached.FieldId);
        Assert.Equal(expected: "Status", actual: cached.FieldName);
        Assert.Equal(expected: options.Count, actual: cached.OptionIds.Count);
        Assert.All(
            collection: StatusOptionLookups,
            action: name => Assert.Equal(expected: original.FindOptionId(name), actual: cached.FindOptionId(name))
        );
        Assert.Throws<AmbiguousStatusOptionException>(() => cached.FindOptionId("done"));
    }

    [Fact]
    public void ItemIdsAreKeyedByKindAndNumber()
    {
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 5, itemId: "PVTI_issue");
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.PULL_REQUEST, number: 5, itemId: "PVTI_pr");

        Assert.Equal(expected: "PVTI_issue", actual: this.GetItemId(kind: WorkItemKind.ISSUE, number: 5));
        Assert.Equal(expected: "PVTI_pr", actual: this.GetItemId(kind: WorkItemKind.PULL_REQUEST, number: 5));
        Assert.Null(this.GetItemId(kind: WorkItemKind.ISSUE, number: 6));
    }

    [Fact]
    public void LaterUpdatesKeepEarlierValues()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1, itemId: "PVTI_one");
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1, itemId: "PVTI_replaced");

        Assert.Equal(expected: Project, actual: this._cache.GetProject(REPOSITORY));
        Assert.Equal(expected: "PVTI_replaced", actual: this.GetItemId(kind: WorkItemKind.ISSUE, number: 1));
    }

    [Fact]
    public void InvalidateDropsOnlyThatRepository()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1, itemId: "PVTI_one");
        this._cache.SetProject(repository: OTHER_REPOSITORY, project: Project);

        this._cache.Invalidate("CREDFETO/credfeto-workflow-mcp");

        Assert.Null(this._cache.GetProject(REPOSITORY));
        Assert.Null(this._cache.GetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1));
        Assert.Equal(expected: Project, actual: this._cache.GetProject(OTHER_REPOSITORY));
    }

    [Fact]
    public void EntriesAreInvisibleToADifferentCaller()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 1, itemId: "PVTI_one");

        this.ActAs(OTHER_CALLER_TOKEN);

        Assert.Null(this._cache.GetProject(REPOSITORY));
        Assert.Null(this.GetItemId(kind: WorkItemKind.ISSUE, number: 1));
    }

    [Fact]
    public void SameTokenOnALaterRequestHitsTheCache()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);

        this.ActAs(CallerContext.Token);

        Assert.Equal(expected: Project, actual: this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public void InvalidateDropsOnlyTheCurrentCallersEntry()
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this.ActAs(OTHER_CALLER_TOKEN);
        this._cache.SetProject(repository: REPOSITORY, project: Project);

        this._cache.Invalidate(REPOSITORY);

        Assert.Null(this._cache.GetProject(REPOSITORY));
        this.ActAs(CallerContext.Token);
        Assert.Equal(expected: Project, actual: this._cache.GetProject(REPOSITORY));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "Basic dXNlcjpwYXNz")]
    [InlineData(true, "Bearer ")]
    public void MissingCallerTokenIsRejected(bool hasHttpContext, string? authorization)
    {
        this._cache.SetProject(repository: REPOSITORY, project: Project);
        this._httpContextAccessor.HttpContext = hasHttpContext ? CallerContext.WithAuthorization(authorization) : null;

        Assert.Throws<CallerTokenMissingException>(() => this._cache.GetProject(REPOSITORY));
        Assert.Throws<CallerTokenMissingException>(() =>
            this._cache.SetProject(repository: REPOSITORY, project: Project)
        );
        Assert.Throws<CallerTokenMissingException>(() => this._cache.Invalidate(REPOSITORY));
    }

    [Fact]
    public async Task ConcurrentUpdatesAreAllKeptAsync()
    {
        const int itemCount = 200;

        await Parallel.ForAsync(
            fromInclusive: 0,
            toExclusive: itemCount,
            cancellationToken: this.CancellationToken(),
            body: (number, _) =>
            {
                this._cache.SetItemId(
                    repository: REPOSITORY,
                    kind: WorkItemKind.ISSUE,
                    number: number,
                    itemId: ItemId(number)
                );

                return ValueTask.CompletedTask;
            }
        );

        Assert.All(
            collection: Enumerable.Range(start: 0, count: itemCount),
            action: number =>
                Assert.Equal(expected: ItemId(number), actual: this.GetItemId(kind: WorkItemKind.ISSUE, number: number))
        );
    }

    private void ActAs(string token)
    {
        this._httpContextAccessor.HttpContext = CallerContext.WithAuthorization("Bearer " + token);
    }

    private string? GetItemId(WorkItemKind kind, int number)
    {
        return this._cache.GetItemId(repository: REPOSITORY, kind: kind, number: number);
    }

    private static string ItemId(int number)
    {
        return "PVTI_" + number.ToString(CultureInfo.InvariantCulture);
    }
}
