using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.BodyRules;

// Holds only the rule and line number: the comment text is never surfaced so a refusal cannot echo it back
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("Rule={Rule}, Line={LineNumber}")]
internal readonly record struct CommentRefusal(string Rule, int? LineNumber);
