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
                "body_check",
                "branch_required_checks",
                "issue_close",
                "issue_comments",
                "issue_create",
                "issue_list",
                "issue_search",
                "issue_view",
                "label_add",
                "label_remove",
                "ping",
                "pr_checks",
                "pr_comments",
                "pr_create",
                "pr_list",
                "pr_reply",
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
    [InlineData("pr_list")]
    [InlineData("branch_required_checks")]
    [InlineData("issue_view")]
    [InlineData("issue_comments")]
    [InlineData("issue_list")]
    [InlineData("issue_search")]
    [InlineData("pr_checks")]
    [InlineData("workflow_status_check")]
    [InlineData("body_check")]
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
    public void PrListTakesRepoAndOptionalFilters()
    {
        JsonElement schema = this.GetTool("pr_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["head", "issue", "labels", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
        Assert.Contains(
            expectedSubstring: "\"array\"",
            actualString: schema.GetProperty("properties").GetProperty("labels").GetProperty("type").GetRawText(),
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
    public void IssueCloseTakesRepoAndIssueAndReasonAndOptionalComment()
    {
        JsonElement schema = this.GetTool("issue_close").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["comment", "issue", "reason", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["issue", "reason", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueCloseIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("issue_close");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "issue_close writes the issue");
        Assert.True(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "issue_close is destructive");
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "issue_close is idempotent");
    }

    [Fact]
    public void IssueSearchTakesRepoAndTextAndOptionalStateAndLimit()
    {
        JsonElement schema = this.GetTool("issue_search").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["limit", "repo", "state", "text"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "text"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueListTakesRepo()
    {
        JsonElement schema = this.GetTool("issue_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void BodyCheckTakesRepoAndBody()
    {
        JsonElement schema = this.GetTool("body_check").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["body", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["body", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueCreateTakesRepoPriorityTitleBodyAndOptionalLabels()
    {
        JsonElement schema = this.GetTool("issue_create").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["body", "labels", "priority", "repo", "title"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["body", "priority", "repo", "title"], actual: RequiredNames(schema));
        Assert.Contains(
            expectedSubstring: "\"array\"",
            actualString: schema.GetProperty("properties").GetProperty("labels").GetProperty("type").GetRawText(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public void IssueCreateIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("issue_create");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "issue_create writes an issue");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "issue_create is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "issue_create is not idempotent");
    }

    [Fact]
    public void PrCreateTakesRepoHeadTitleBodyAndOptionalBase()
    {
        JsonElement schema = this.GetTool("pr_create").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["base", "body", "head", "repo", "title"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["body", "head", "repo", "title"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrCreateIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_create");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_create writes a pull request");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "pr_create is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "pr_create is not idempotent");
    }

    [Fact]
    public void PrReplyTakesRepoAndPrAndCommentIdAndBody()
    {
        JsonElement schema = this.GetTool("pr_reply").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["body", "commentId", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["body", "commentId", "pr", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrReplyIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_reply");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_reply writes a comment");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "pr_reply is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "pr_reply is not idempotent");
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

    [Theory]
    [InlineData("label_add")]
    [InlineData("label_remove")]
    public void LabelToolsTakeRepoAndLabelsAndOptionalTarget(string name)
    {
        JsonElement schema = this.GetTool(name).ProtocolTool.InputSchema;

        Assert.Equal(expected: ["issue", "labels", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["labels", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void LabelAddIsAnIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("label_add");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "label_add writes labels");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "label_add is not destructive");
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "label_add is idempotent");
    }

    [Fact]
    public void LabelRemoveIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("label_remove");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "label_remove writes labels");
        Assert.True(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "label_remove is destructive");
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "label_remove is idempotent");
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
            .AddMockedService<IPullRequestListService>()
            .AddMockedService<ILabelService>()
            .AddMockedService<IIssueService>()
            .AddMockedService<IIssueCreateService>()
            .AddMockedService<IIssueSearchService>()
            .AddMockedService<IIssueListService>()
            .AddMockedService<IPrCreateService>()
            .AddMockedService<IPrReplyService>()
            .AddMockedService<IBodyCheckService>()
            .AddMockedService<IPrChecksService>()
            .AddMockedService<IWorkflowStatusCheckService>()
            .AddMockedService<IWorkflowStatusSetService>()
            .AddMcpTools();
    }
}
