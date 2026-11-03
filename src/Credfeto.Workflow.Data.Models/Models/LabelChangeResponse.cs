using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}#{Number}")]
public sealed record LabelChangeResponse(string Repository, int Number, IReadOnlyList<string> Labels);
