using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("#{Number}: {Title}")]
public sealed record IssueSearchEntry(int Number, string State, string Title, IReadOnlyList<string> Labels);
