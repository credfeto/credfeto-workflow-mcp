using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        Assert.Equal(expected: ["branch_required_checks", "ping", "pr_comments", "pr_view"], actual: names);
    }

    [Theory]
    [InlineData("pr_comments")]
    [InlineData("pr_view")]
    [InlineData("branch_required_checks")]
    public void ReadingToolsMustBeReadOnly(string name)
    {
        McpServerTool tool = this.GetTool(name);

        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: name + " should be read only");
    }

    [Fact]
    public void PrViewTakesRepoAndPrAndOptionalFields()
    {
        JsonElement schema = this.GetTool("pr_view").ProtocolTool.InputSchema;

        string[] properties =
        [
            .. schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(static p => p.Name)
                .Order(StringComparer.Ordinal),
        ];
        string[] required =
        [
            .. schema
                .GetProperty("required")
                .EnumerateArray()
                .Select(static r => r.GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(expected: ["fields", "pr", "repo"], actual: properties);
        Assert.Equal(expected: ["pr", "repo"], actual: required);
        Assert.Contains(
            expectedSubstring: "\"array\"",
            actualString: schema.GetProperty("properties").GetProperty("fields").GetProperty("type").GetRawText(),
            comparisonType: StringComparison.Ordinal
        );
    }

    private McpServerTool GetTool(string name)
    {
        return this
            .ServiceProvider.GetServices<McpServerTool>()
            .Single(tool => StringComparer.Ordinal.Equals(x: tool.ProtocolTool.Name, y: name));
    }

    private static IServiceCollection Configure(IServiceCollection services)
    {
        // Registered so each tool binds its reader as a service instead of a JSON argument
        return services
            .AddMockedService<IPullRequestCommentsReader>()
            .AddMockedService<IRequiredChecksReader>()
            .AddMockedService<IPullRequestService>()
            .AddMcpTools();
    }
}
