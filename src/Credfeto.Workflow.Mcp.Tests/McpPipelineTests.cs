using System.Threading.Tasks;
using Credfeto.Workflow.GitHub;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class McpPipelineTests : TestBase
{
    [Theory]
    [InlineData("/mcp", StatusCodes.Status401Unauthorized)]
    [InlineData("/MCP/", StatusCodes.Status401Unauthorized)]
    [InlineData("/mcpx", StatusCodes.Status404NotFound)]
    [InlineData("/ping", StatusCodes.Status404NotFound)]
    public static async Task OnlyMcpRequestsRequireCallerTokenAsync(string path, int expectedStatusCode)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        _ = builder.Services.AddGitHub().AddMcpTools();

        await using WebApplication app = builder.Build();

        app.MapMcpEndpoint();

        // Only the middleware pipeline is built here, without routing, so anything the token requirement
        // lets through falls to the terminal 404
        RequestDelegate pipeline = ((IApplicationBuilder)app).Build();

        DefaultHttpContext context = new() { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;

        await pipeline(context);

        Assert.Equal(expected: expectedStatusCode, actual: context.Response.StatusCode);
    }
}
