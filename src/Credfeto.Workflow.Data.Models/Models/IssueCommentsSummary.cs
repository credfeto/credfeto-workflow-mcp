using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("Printed={Printed}, Matched={Matched}, Total={Total}, Omitted={Omitted}")]
public sealed record IssueCommentsSummary(int Printed, int Matched, int Total, int Omitted);
