using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.PullRequests;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Step}")]
internal readonly record struct PullRequestWriteFailure(string Step, Exception Exception);
