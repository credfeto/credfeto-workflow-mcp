using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.Mcp.Modes;

// Resolved once per request, alongside the caller identity, so every filter and tool sees the same mode
[DebuggerDisplay("{Mode}")]
public sealed record RequestedToolMode(ToolModes Mode)
{
    public static void Set(HttpContext context, ToolModes mode)
    {
        context.Features.Set(new RequestedToolMode(mode));
    }

    public static ToolModes Of(HttpContext? context)
    {
        // Only a request through the /mcp endpoint has a mode, and that endpoint always sets one
        return context?.Features.Get<RequestedToolMode>()?.Mode ?? ToolModes.All;
    }
}
