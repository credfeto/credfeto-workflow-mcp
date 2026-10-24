using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[DebuggerDisplay("Count={Count}")]
public readonly record struct TestCount(int Count);
