using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Board is the Workflow status set, or "no board" when the repository has no Workflow board
[DebuggerDisplay("{CommentUrl}: {Board}")]
public sealed record IssuePostPlanResponse(Uri CommentUrl, IReadOnlyList<string> Labels, string Board);
