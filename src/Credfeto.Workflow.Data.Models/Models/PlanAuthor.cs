using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Login}: {Association}")]
public sealed record PlanAuthor(string Login, string Association, Uri Url);
