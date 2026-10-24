using System.Collections.Immutable;
using System.Diagnostics;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Cache;

[DebuggerDisplay("Project={Project}, StatusField={StatusField}, Items={ItemIds.Count}")]
internal sealed record RepositoryCacheEntry(
    WorkflowProject? Project,
    WorkflowStatusField? StatusField,
    ImmutableDictionary<WorkItemKey, string> ItemIds
)
{
    public static RepositoryCacheEntry Empty { get; } = new(Project: null, StatusField: null, ItemIds: []);
}
