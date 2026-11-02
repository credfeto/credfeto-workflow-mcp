using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{ItemUrl}: {Status}")]
public sealed record WorkflowStatusSetResponse(Uri ItemUrl, string Status);
