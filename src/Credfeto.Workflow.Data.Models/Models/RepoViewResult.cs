using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// DefaultBranch is empty for an empty repository and Description is empty when there is none; a null merge method
// means GitHub did not say, never that the method is disallowed
[DebuggerDisplay("{Visibility}: {DefaultBranch}")]
public sealed record RepoViewResult(
    string DefaultBranch,
    string Visibility,
    bool? AllowMergeCommit,
    bool? AllowSquashMerge,
    bool? AllowRebaseMerge,
    string Description
);
