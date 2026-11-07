using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Name} {Status} {Conclusion}")]
public sealed record RunJobEntry(long Id, string Name, string Status, string Conclusion);
