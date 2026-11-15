using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Status is only given in the NameOnly mode, and Additions and Deletions only in the Stat mode
[DebuggerDisplay("{Status} {Path} +{Additions} -{Deletions}")]
public sealed record PrDiffFile(string? Status, string Path, int? Additions, int? Deletions);
