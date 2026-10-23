using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[DebuggerDisplay("Owner={Owner}, Number={Number}")]
public sealed record TestVariables(string Owner, int Number);
