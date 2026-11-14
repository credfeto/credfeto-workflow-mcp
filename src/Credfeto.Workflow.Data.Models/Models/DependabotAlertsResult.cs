using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Alerts.Count}")]
public sealed record DependabotAlertsResult(string Repository, IReadOnlyList<DependabotAlertEntry> Alerts);
