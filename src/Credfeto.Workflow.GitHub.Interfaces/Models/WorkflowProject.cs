using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

[DebuggerDisplay("ProjectNodeId={ProjectNodeId}, Number={Number}, Owner={Owner}")]
public sealed record WorkflowProject(string ProjectNodeId, int Number, string Owner);
