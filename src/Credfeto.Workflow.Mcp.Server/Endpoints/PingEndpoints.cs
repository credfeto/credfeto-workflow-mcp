using Credfeto.Workflow.Data.Models.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Credfeto.Workflow.Mcp.Server.Endpoints;

public static class PingEndpoints
{
    public static void MapPingEndpoints(this IEndpointRouteBuilder app)
    {
        // A lambda rather than a bare method group: passing a method group from another
        // assembly crashes the built-in RouteHandlerAnalyzer with AD0001 during build.
        _ = app.MapGet(pattern: "/ping", handler: static () => WorkflowMcpTools.Ping())
            .WithName("Ping")
            .Produces<PingResponse>()
            .AllowAnonymous();
    }
}
