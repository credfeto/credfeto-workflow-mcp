using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[DebuggerDisplay("Id={Id}, Name={Name}")]
public sealed record TestItem(int Id, string Name);
