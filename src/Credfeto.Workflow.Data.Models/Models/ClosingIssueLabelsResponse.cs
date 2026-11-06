using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("Labels={Labels.Count}, Complete={Complete}, FailedReferences={FailedReferences.Count}")]
public sealed record ClosingIssueLabelsResponse(
    IReadOnlyList<string> Labels,
    bool Complete,
    IReadOnlyList<string> FailedReferences
);
