using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Reset is the UTC time the limit resets, as yyyy-MM-ddTHH:mm:ssZ
[DebuggerDisplay("{Remaining}/{Limit} until {Reset}")]
public sealed record AuthRateLimit(long Remaining, long Limit, string Reset);
