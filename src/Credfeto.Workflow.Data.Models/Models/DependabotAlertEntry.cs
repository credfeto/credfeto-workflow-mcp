using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Number}: {Severity} {State} {Ecosystem} {Package}")]
public sealed record DependabotAlertEntry(
    int Number,
    string Severity,
    string State,
    string Ecosystem,
    string Package,
    string ManifestPath,
    string VulnerableRange,
    string FirstPatchedVersion,
    string GhsaId
);
