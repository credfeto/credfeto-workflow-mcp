using System.Collections.Generic;
using FunFair.Test.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class DependencyInjectionTests : DependencyInjectionTestsBase
{
    public DependencyInjectionTests(ITestOutputHelper output)
        : base(output: output, dependencyInjectionRegistration: Configure) { }

    [Fact]
    public void PingToolMustBeRegistered()
    {
        IEnumerable<McpServerTool> tools = this.ServiceProvider.GetServices<McpServerTool>();

        McpServerTool tool = Assert.Single(tools);

        Assert.Equal(expected: "ping", actual: tool.ProtocolTool.Name);
    }

    private static IServiceCollection Configure(IServiceCollection services)
    {
        return services.AddMcpTools();
    }
}
