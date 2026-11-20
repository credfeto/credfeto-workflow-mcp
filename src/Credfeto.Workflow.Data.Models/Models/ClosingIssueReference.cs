using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}#{Number}")]
public sealed record ClosingIssueReference(string Repository, int Number);
