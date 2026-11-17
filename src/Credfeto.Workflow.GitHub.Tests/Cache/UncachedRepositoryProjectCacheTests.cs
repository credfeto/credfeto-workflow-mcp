using System;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Cache;

public sealed class UncachedRepositoryProjectCacheTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private readonly UncachedRepositoryProjectCache _cache = new();

    [Fact]
    public void ProjectIsNeverKept()
    {
        this._cache.SetProject(
            repository: REPOSITORY,
            project: new WorkflowProject(ProjectNodeId: "PVT_kwDOA", Number: 7, Owner: "credfeto")
        );

        Assert.Null(this._cache.GetProject(REPOSITORY));
    }

    [Fact]
    public void StatusFieldIsNeverKept()
    {
        this._cache.SetStatusField(
            repository: REPOSITORY,
            statusField: new WorkflowStatusField(
                FieldId: "PVTSSF_status",
                FieldName: "Status",
                OptionIds: new Dictionary<string, string>(StringComparer.Ordinal) { ["Not Started"] = "opt" }
            )
        );

        Assert.Null(this._cache.GetStatusField(REPOSITORY));
    }

    [Fact]
    public void ItemIdIsNeverKept()
    {
        this._cache.SetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 42, itemId: "PVTI_lADOA");

        Assert.Null(this._cache.GetItemId(repository: REPOSITORY, kind: WorkItemKind.ISSUE, number: 42));
    }

    [Fact]
    public void InvalidatingNothingLeavesNothing()
    {
        this._cache.Invalidate(REPOSITORY);

        Assert.Null(this._cache.GetProject(REPOSITORY));
        Assert.Null(this._cache.GetStatusField(REPOSITORY));
    }
}
