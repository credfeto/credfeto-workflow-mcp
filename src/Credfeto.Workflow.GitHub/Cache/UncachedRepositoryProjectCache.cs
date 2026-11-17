using System.Diagnostics;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Cache;

// What the board token reads is never shared with callers, so a caller who cannot see the board never learns of it
// from the cache
[DebuggerDisplay("Uncached")]
public sealed class UncachedRepositoryProjectCache : IRepositoryProjectCache
{
    public WorkflowProject? GetProject(string repository)
    {
        return null;
    }

    public void SetProject(string repository, WorkflowProject project)
    {
        // Nothing is kept
    }

    public WorkflowStatusField? GetStatusField(string repository)
    {
        return null;
    }

    public void SetStatusField(string repository, WorkflowStatusField statusField)
    {
        // Nothing is kept
    }

    public string? GetItemId(string repository, WorkItemKind kind, int number)
    {
        return null;
    }

    public void SetItemId(string repository, WorkItemKind kind, int number, string itemId)
    {
        // Nothing is kept
    }

    public void Invalidate(string repository)
    {
        // Nothing is kept
    }
}
