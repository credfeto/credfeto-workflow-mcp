using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Tags;

// A git object GitHub named, once its type and SHA have been checked
[DebuggerDisplay("{Type}: {Sha}")]
internal sealed record GitTarget(string Type, string Sha);
