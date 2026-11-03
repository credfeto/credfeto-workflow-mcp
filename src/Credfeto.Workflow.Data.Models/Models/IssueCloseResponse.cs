using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Issue}: {Outcome}")]
public sealed record IssueCloseResponse(string Issue, string Outcome, string Reason, Uri Url);
