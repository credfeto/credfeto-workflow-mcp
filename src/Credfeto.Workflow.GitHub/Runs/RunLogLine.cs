using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{Text}")]
public readonly record struct RunLogLine(string Text, bool Redacted, bool Masked);
