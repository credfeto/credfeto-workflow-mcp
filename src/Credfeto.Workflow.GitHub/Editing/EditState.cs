using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Editing;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Url}: {Title}")]
internal readonly record struct EditState(Uri Url, string Title, string? Body, IReadOnlyList<string> Assignees);
