using System;
using System.Linq;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using FunFair.Test.Common;
using FunFair.Test.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class McpSetupTests : TestBase
{
    [Fact]
    public async Task MapMcpEndpointRegistersMcpRouteAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        // Registered so each tool binds its reader as a service instead of a JSON argument
        _ = builder
            .Services.AddMockedService<IPullRequestCommentsReader>()
            .AddMockedService<IRequiredChecksReader>()
            .AddMcpTools();

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
