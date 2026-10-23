using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[DebuggerDisplay("Login={Login}")]
public sealed record TestData(string Login);
