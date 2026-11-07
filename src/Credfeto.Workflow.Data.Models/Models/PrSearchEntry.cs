using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("#{Number}: {Title}")]
public sealed record PrSearchEntry(int Number, string State, string HeadBranch, string Title);
