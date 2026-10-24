using System.IO;
using System.Text;
using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Server.Endpoints;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class PingEndpointsTests : TestBase
{
    [Fact]
    public async Task PingReturnsPongAsync()
    {
        await using WebApplication app = TestAppFactory.BuildApp();

        app.MapPingEndpoints();

        RouteEndpoint endpoint = Assert.Single(EndpointLookup.FindEndpoints(app: app, pattern: "/ping"));
        RequestDelegate requestDelegate = AssertReallyNotNull(endpoint.RequestDelegate);

        await using MemoryStream body = new();
        DefaultHttpContext context = new() { RequestServices = app.Services, Response = { Body = body } };
        context.Request.Method = HttpMethods.Get;

        await requestDelegate(context);

        Assert.Equal(expected: StatusCodes.Status200OK, actual: context.Response.StatusCode);
        Assert.Equal(expected: """{"value":"pong"}""", actual: Encoding.UTF8.GetString(body.ToArray()));
    }
}
