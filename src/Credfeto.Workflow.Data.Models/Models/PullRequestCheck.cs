using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Kind is check-run (status and conclusion set) or status (state set); the fields that do not apply are empty
[DebuggerDisplay("{Kind} {Name}: {Status} {Conclusion} {State}")]
public sealed record PullRequestCheck(string Kind, string Name, string Status, string Conclusion, string State);
