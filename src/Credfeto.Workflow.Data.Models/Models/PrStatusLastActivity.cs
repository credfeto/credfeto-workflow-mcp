using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{At} by {Author}")]
public sealed record PrStatusLastActivity(string At, string Author);
