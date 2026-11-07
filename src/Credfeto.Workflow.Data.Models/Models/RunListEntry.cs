using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Workflow} {Status} {Conclusion}")]
public sealed record RunListEntry(
    long Id,
    string Workflow,
    string Status,
    string Conclusion,
    string HeadSha,
    string Event,
    string CreatedAt
);
