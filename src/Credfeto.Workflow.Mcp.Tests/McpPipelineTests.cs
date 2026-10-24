using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class McpPipelineTests : TestBase
{
    [Fact]
    public static async Task EveryMcpEndpointRejectsRequestsWithoutCallerTokenAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        _ = builder.Services.AddGitHub().AddMcpTools();

        await using WebApplication app = builder.Build();

        app.MapMcpEndpoint();

        IReadOnlyList<RouteEndpoint> endpoints = GetEndpoints(app);

        Assert.NotEmpty(endpoints);

        foreach (RouteEndpoint endpoint in endpoints)
        {
            DefaultHttpContext context = new() { RequestServices = app.Services };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/mcp";

            RequestDelegate? handler = endpoint.RequestDelegate;
            Assert.NotNull(handler);

            await handler(context);

            Assert.Equal(expected: StatusCodes.Status401Unauthorized, actual: context.Response.StatusCode);
            Assert.Equal(expected: "Bearer", actual: context.Response.Headers.WWWAuthenticate.ToString());
        }
    }

    private static IReadOnlyList<RouteEndpoint> GetEndpoints(WebApplication app)
    {
        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(static dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>(),
        ];
    }
}
