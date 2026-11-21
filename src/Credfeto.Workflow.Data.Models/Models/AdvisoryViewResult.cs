using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Published and Withdrawn are GitHub's ISO-8601 text, as cfwf prints them; Withdrawn is empty when not withdrawn
[DebuggerDisplay("{GhsaId}: {Severity} {Packages.Count}")]
public sealed record AdvisoryViewResult(
    string GhsaId,
    string Severity,
    string Summary,
    string Published,
    string Withdrawn,
    IReadOnlyList<AdvisoryAffectedPackage> Packages
);
