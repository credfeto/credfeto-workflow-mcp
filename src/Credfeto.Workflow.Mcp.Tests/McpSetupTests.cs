using System;
using System.Linq;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class McpSetupTests : TestBase
{
    [Fact]
    public async Task MapMcpEndpointRegistersMcpRouteAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        _ = builder.Services.AddMcpTools();

        await using WebApplication app = builder.Build();

        app.MapMcpEndpoint();

        string[] patterns =
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(static dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty),
        ];

        // MapMcp maps a route group, so the streamable HTTP endpoint is the group root
        Assert.Contains(expected: "/mcp/", collection: patterns, comparer: StringComparer.Ordinal);
    }
}
