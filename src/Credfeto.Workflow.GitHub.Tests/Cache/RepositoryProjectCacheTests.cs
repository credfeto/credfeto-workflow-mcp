using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Cache;

public sealed class RepositoryProjectCacheTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string OTHER_REPOSITORY = "credfeto/other";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_project", Number: 7, Owner: "credfeto");

    private readonly RepositoryProjectCache _cache = new();

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
    public void StatusFieldOptionsAreLookedUpCaseInsensitively()
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal)
        {
            ["In Progress"] = "OPT_progress",
            ["Done"] = "OPT_done",
        };

        this._cache.SetStatusField(
            repository: REPOSITORY,
            statusField: new WorkflowStatusField(FieldId: "PVTSSF_status", FieldName: "Status", OptionIds: options)
        );

        WorkflowStatusField statusField = AssertReallyNotNull(this._cache.GetStatusField(REPOSITORY));
        Assert.Equal(expected: "PVTSSF_status", actual: statusField.FieldId);
        Assert.Equal(expected: "Status", actual: statusField.FieldName);
        Assert.Equal(expected: "OPT_progress", actual: statusField.OptionIds["in progress"]);
        Assert.Equal(expected: "OPT_done", actual: statusField.OptionIds["DONE"]);
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

    private string? GetItemId(WorkItemKind kind, int number)
    {
        return this._cache.GetItemId(repository: REPOSITORY, kind: kind, number: number);
    }

    private static string ItemId(int number)
    {
        return "PVTI_" + number.ToString(CultureInfo.InvariantCulture);
    }
}
