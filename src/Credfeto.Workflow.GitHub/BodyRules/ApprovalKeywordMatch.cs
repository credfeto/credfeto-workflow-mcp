using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.BodyRules;

// Holds only the line number: the matched text is never surfaced so a refusal cannot echo the keyword back
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("Line={LineNumber}")]
public readonly record struct ApprovalKeywordMatch(int LineNumber);
