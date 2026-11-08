using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Editing;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Subject}: {Rule} (line {LineNumber})")]
internal readonly record struct EditRefusal(string Subject, string Rule, int? LineNumber);
