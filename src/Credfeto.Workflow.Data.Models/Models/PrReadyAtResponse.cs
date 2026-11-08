using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Source is ready-for-review when the pull request was last marked ready, or created when it never was
[DebuggerDisplay("{ReadyAt} ({Source})")]
public sealed record PrReadyAtResponse(string ReadyAt, string Source);
