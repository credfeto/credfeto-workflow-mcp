using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository} {Name}: {Outcome}")]
public sealed record LabelCreateResponse(
    LabelCreateOutcome Outcome,
    string Repository,
    string Name,
    string Colour,
    string Description
);
