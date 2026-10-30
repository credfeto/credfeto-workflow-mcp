using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces;
using FunFair.Test.Common;
using FunFair.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class DependencyInjectionTests : DependencyInjectionTestsBase
{
    public DependencyInjectionTests(ITestOutputHelper output)
        : base(output: output, dependencyInjectionRegistration: Configure) { }

    [Fact]
    public void AllToolsMustBeRegistered()
    {
        IEnumerable<McpServerTool> tools = this.ServiceProvider.GetServices<McpServerTool>();

        string[] names = [.. tools.Select(static tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(expected: ["branch_required_checks", "ping", "pr_comments"], actual: names);
    }

    [Theory]
    [InlineData("pr_comments")]
    [InlineData("branch_required_checks")]
    public void ReadingToolsMustBeReadOnly(string name)
    {
        McpServerTool tool = this
            .ServiceProvider.GetServices<McpServerTool>()
            .Single(tool => StringComparer.Ordinal.Equals(x: tool.ProtocolTool.Name, y: name));

        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: name + " should be read only");
    }

    private static IServiceCollection Configure(IServiceCollection services)
    {
        // Registered so each tool binds its reader as a service instead of a JSON argument
        return services
            .AddMockedService<IPullRequestCommentsReader>()
            .AddMockedService<IRequiredChecksReader>()
            .AddMcpTools();
    }
}
