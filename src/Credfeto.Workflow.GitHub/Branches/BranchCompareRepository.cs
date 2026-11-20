using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

// Ref is the base branch, which GitHub answers as null when it does not exist
[DebuggerDisplay("{Ref}")]
internal sealed record BranchCompareRepository([property: JsonPropertyName("ref")] BranchCompareRef? Ref);
