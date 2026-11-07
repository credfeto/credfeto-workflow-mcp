using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Path}: {Matches}")]
public sealed record CodeSearchEntry(string Path, int Matches);
