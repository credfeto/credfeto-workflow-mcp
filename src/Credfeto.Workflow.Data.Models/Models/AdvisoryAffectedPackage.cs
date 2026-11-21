using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// VulnerableRange and FirstPatched are empty when GitHub gives none, such as an advisory with no fix yet
[DebuggerDisplay("{Ecosystem}/{Package}: {VulnerableRange} {FirstPatched}")]
public sealed record AdvisoryAffectedPackage(
    string Ecosystem,
    string Package,
    string VulnerableRange,
    string FirstPatched
);
