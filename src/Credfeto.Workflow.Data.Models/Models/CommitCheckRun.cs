using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Conclusion is null until the run has finished; AppId is null when GitHub names no app for the run
[DebuggerDisplay("{Id}: {Name} {Status} {Conclusion}")]
public sealed record CommitCheckRun(long Id, string Name, string Status, string? Conclusion, long? AppId);
