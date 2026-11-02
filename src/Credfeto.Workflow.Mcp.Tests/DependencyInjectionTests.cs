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

        Assert.Equal(
            expected:
            [
                "branch_required_checks",
                "issue_comments",
                "issue_view",
                "ping",
                "pr_checks",
                "pr_comments",
                "pr_view",
                "workflow_status_check",
                "workflow_status_set",
            ],
            actual: names
        );
    }

    [Theory]
    [InlineData("pr_comments")]
    [InlineData("pr_view")]
    [InlineData("branch_required_checks")]
    [InlineData("issue_view")]
    [InlineData("issue_comments")]
    [InlineData("pr_checks")]
    [InlineData("workflow_status_check")]
    public void ReadingToolsMustBeReadOnly(string name)
    {
        McpServerTool tool = this.GetTool(name);

        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: name + " should be read only");
    }

    [Fact]
    public void PrViewTakesRepoAndPrAndOptionalFields()
    {
        JsonElement schema = this.GetTool("pr_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["fields", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
        Assert.Contains(
            expectedSubstring: "\"array\"",
            actualString: schema.GetProperty("properties").GetProperty("fields").GetProperty("type").GetRawText(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public void IssueViewTakesRepoAndIssue()
    {
        JsonElement schema = this.GetTool("issue_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["issue", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["issue", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueCommentsTakesRepoAndIssueAndOptionalFilters()
    {
        JsonElement schema = this.GetTool("issue_comments").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["author", "issue", "repo", "since"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["issue", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrChecksTakesRepoAndOptionalTargets()
    {
        JsonElement schema = this.GetTool("pr_checks").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["all", "branch", "pr", "repo", "sha"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowStatusCheckTakesRepoAndOptionalWorkItems()
    {
        JsonElement schema = this.GetTool("workflow_status_check").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["issue", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowStatusSetTakesRepoAndStatusAndOptionalWorkItems()
    {
        JsonElement schema = this.GetTool("workflow_status_set").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["issue", "pr", "repo", "status"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "status"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowStatusSetIsAnIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("workflow_status_set");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "workflow_status_set writes the board");
        Assert.False(
            tool.ProtocolTool.Annotations?.DestructiveHint,
            userMessage: "workflow_status_set is not destructive"
        );
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "workflow_status_set is idempotent");
    }

    private static string[] PropertyNames(JsonElement schema)
    {
        return
        [
            .. schema
                .GetProperty("properties")
                .EnumerateObject()
                .Select(static p => p.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string[] RequiredNames(JsonElement schema)
    {
        return
        [
            .. schema
                .GetProperty("required")
                .EnumerateArray()
                .Select(static r => r.GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];
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
            .AddMockedService<IIssueService>()
            .AddMockedService<IPrChecksService>()
            .AddMockedService<IWorkflowStatusCheckService>()
            .AddMockedService<IWorkflowStatusSetService>()
            .AddMcpTools();
    }
}
