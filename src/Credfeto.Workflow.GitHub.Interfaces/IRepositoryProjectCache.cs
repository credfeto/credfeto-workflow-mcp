using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IRepositoryProjectCache
{
    WorkflowProject? GetProject(string repository);

    void SetProject(string repository, WorkflowProject project);

    WorkflowStatusField? GetStatusField(string repository);

    void SetStatusField(string repository, WorkflowStatusField statusField);

    string? GetItemId(string repository, WorkItemKind kind, int number);

    void SetItemId(string repository, WorkItemKind kind, int number, string itemId);

    void Invalidate(string repository);
}
