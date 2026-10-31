using System.Diagnostics;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("{Url}: {FieldName}={OptionId}")]
internal sealed record BoardWriteRequest(
    string Owner,
    string Repo,
    string Repository,
    WorkItemKind Kind,
    int Number,
    string Url,
    WorkflowProject Project,
    string FieldId,
    string FieldName,
    string OptionId
);
