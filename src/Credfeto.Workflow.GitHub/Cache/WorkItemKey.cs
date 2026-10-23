using System.Diagnostics;
using System.Runtime.InteropServices;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Cache;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("Kind={Kind}, Number={Number}")]
internal readonly record struct WorkItemKey(WorkItemKind Kind, int Number);
