using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Url}")]
public sealed record IssueCreateResponse(Uri Url);
