using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.RepoFiles;

[DebuggerDisplay("{Repository}: {Path}")]
internal sealed record RepoFileRequest(string Repository, string Path, string? GitRef);
