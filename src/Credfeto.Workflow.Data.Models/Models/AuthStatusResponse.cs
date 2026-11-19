using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Host is the MCP host the caller reached and Version is the GitHub API version the server sends; both are empty
// when there is none. RateLimits is only present when the rate limits were asked for
[DebuggerDisplay("{Login} on {Host}")]
public sealed record AuthStatusResponse(string Login, string Host, string Version, AuthRateLimits? RateLimits);
