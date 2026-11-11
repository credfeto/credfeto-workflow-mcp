using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// EnabledAt is - when auto-merge was never enabled, matching what the cfwf command prints
[DebuggerDisplay("#{Pr}: {EnabledAt}")]
public sealed record PrAutoMergeAtResponse(int Pr, string EnabledAt);
