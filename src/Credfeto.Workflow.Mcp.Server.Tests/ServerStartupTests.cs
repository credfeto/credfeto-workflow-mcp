using System;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ServerStartupTests : TestBase
{
    [Theory]
    [InlineData("/ping")]
    [InlineData("/mcp/")]
    public static async Task BuildAppMapsEndpointAsync(string pattern)
    {
        await using WebApplication app = ServerStartup.BuildApp([$"--contentRoot={AppContext.BaseDirectory}"]);

        Assert.NotEmpty(EndpointLookup.FindEndpoints(app: app, pattern: pattern));
    }
}
