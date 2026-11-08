using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Editing;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Outcome}: {Reason}")]
internal readonly record struct EditWrite(EditWriteOutcome Outcome, string Reason);
