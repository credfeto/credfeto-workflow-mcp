using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Issue}: {Outcome}")]
public sealed record IssueReopenResponse(string Issue, string Outcome, Uri Url);
