using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Runs;

// GitHub answers a rerun with an empty object, so there is nothing to read from it
[DebuggerDisplay("Rerun requested")]
internal sealed record RunRerunResponse;
