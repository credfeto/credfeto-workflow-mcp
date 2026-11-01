using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Configuration;

[DebuggerDisplay("PrivateOwners: {PrivateOwners}")]
public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    // Space-separated owners whose repositories are always treated as private when referenced in text
    public string? PrivateOwners { get; set; }
}
