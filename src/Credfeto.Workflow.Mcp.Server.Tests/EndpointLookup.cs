using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Routing;

namespace Credfeto.Workflow.Mcp.Server.Tests;

internal static class EndpointLookup
{
    public static IReadOnlyList<RouteEndpoint> FindEndpoints(IEndpointRouteBuilder app, string pattern)
    {
        return
        [
            .. app
                .DataSources.SelectMany(static dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint => StringComparer.Ordinal.Equals(x: endpoint.RoutePattern.RawText, y: pattern)),
        ];
    }
}
