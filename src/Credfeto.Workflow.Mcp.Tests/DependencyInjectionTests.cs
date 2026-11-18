using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using FunFair.Test.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class DependencyInjectionTests : DependencyInjectionTestsBase
{
    private static readonly string[] ExpectedToolNames =
    [
        "body_check",
        "branch_required_checks",
        "closing_issue_labels",
        "code_search",
        "comment_add",
        "issue_add_sub_issue",
        "issue_close",
        "issue_comments",
        "issue_create",
        "issue_edit",
        "issue_list",
        "issue_plan_status",
        "issue_reopen",
        "issue_search",
        "issue_view",
        "label_add",
        "label_create",
        "label_list",
        "label_remove",
        "ping",
        "pr_auto_merge_at",
        "pr_checks",
        "pr_close",
        "pr_comments",
        "pr_create",
        "pr_diff",
        "pr_edit",
        "pr_hand_off",
        "pr_list",
        "pr_ready_at",
        "pr_reopen",
        "pr_reply",
        "pr_review_comment",
        "pr_search",
        "pr_status",
        "pr_view",
        "release_list",
        "release_view",
        "repo_collaborators",
        "repo_dependabot_alerts",
        "repo_file",
        "repo_list",
        "repo_search",
        "repo_view",
        "run_list",
        "run_log",
        "run_rerun",
        "run_view",
        "tag_view",
        "workflow_list",
        "workflow_run",
        "workflow_status_check",
        "workflow_status_set",
    ];

    private static readonly string[] ExpectedIssuesToolNames =
    [
        "body_check",
        "comment_add",
        "issue_add_sub_issue",
        "issue_comments",
        "issue_create",
        "issue_edit",
        "issue_list",
        "issue_plan_status",
        "issue_search",
        "issue_view",
        "label_add",
        "label_list",
        "label_remove",
        "ping",
    ];

    public DependencyInjectionTests(ITestOutputHelper output)
        : base(output: output, dependencyInjectionRegistration: Configure) { }

    [Fact]
    public void AllToolsMustBeRegistered()
    {
        IEnumerable<McpServerTool> tools = this.ServiceProvider.GetServices<McpServerTool>();

        string[] names = [.. tools.Select(static tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(expected: ExpectedToolNames, actual: names);
    }

    [Fact]
    public void EveryToolDeclaresItsModesAndIsAvailableWithoutAMode()
    {
        foreach (McpServerTool tool in this.ServiceProvider.GetServices<McpServerTool>())
        {
            string name = tool.ProtocolTool.Name;

            Assert.Single(tool.Metadata.OfType<InModesAttribute>());
            Assert.True(
                (ToolModeCatalog.ModesOf(tool) & ToolModes.All) == ToolModes.All,
                userMessage: name + " must be available when no X-Mode is given"
            );
        }
    }

    [Fact]
    public void EveryToolIsInTheAllMode()
    {
        ToolModeCatalog catalog = this.ServiceProvider.GetRequiredService<ToolModeCatalog>();

        Assert.Equal(
            expected: ExpectedToolNames,
            actual: catalog.ToolsIn(ToolModes.All),
            comparer: StringComparer.Ordinal
        );
    }

    [Fact]
    public void IssuesModeHasExactlyTheIssueTools()
    {
        ToolModeCatalog catalog = this.ServiceProvider.GetRequiredService<ToolModeCatalog>();

        Assert.Equal(
            expected: ExpectedIssuesToolNames,
            actual: catalog.ToolsIn(ToolModes.Issues),
            comparer: StringComparer.Ordinal
        );
    }

    [Theory]
    [InlineData("issue_close")]
    [InlineData("issue_reopen")]
    [InlineData("label_create")]
    [InlineData("pr_view")]
    [InlineData("run_list")]
    [InlineData("release_list")]
    [InlineData("repo_list")]
    [InlineData("repo_view")]
    [InlineData("code_search")]
    [InlineData("no_such_tool")]
    public void IssuesModeRefusesEverythingElse(string name)
    {
        ToolModeCatalog catalog = this.ServiceProvider.GetRequiredService<ToolModeCatalog>();

        Assert.False(
            catalog.IsAvailable(name: name, mode: ToolModes.Issues),
            userMessage: name + " is not an Issues tool"
        );
    }

    [Fact]
    public void AToolWithoutModesIsInNoMode()
    {
        McpServerTool tool = McpServerTool.Create(
            (Func<string>)(static () => "ok"),
            new McpServerToolCreateOptions { Name = "undeclared" }
        );

        Assert.Equal(expected: ToolModes.None, actual: ToolModeCatalog.ModesOf(tool));
        Assert.False(
            new ToolModeCatalog([tool]).IsAvailable(name: "undeclared", mode: ToolModes.All),
            userMessage: "a tool without modes is never available"
        );
    }

    [Theory]
    [InlineData("pr_comments")]
    [InlineData("pr_view")]
    [InlineData("pr_diff")]
    [InlineData("pr_list")]
    [InlineData("branch_required_checks")]
    [InlineData("issue_view")]
    [InlineData("issue_comments")]
    [InlineData("issue_list")]
    [InlineData("issue_search")]
    [InlineData("pr_search")]
    [InlineData("code_search")]
    [InlineData("repo_search")]
    [InlineData("repo_list")]
    [InlineData("workflow_list")]
    [InlineData("repo_dependabot_alerts")]
    [InlineData("repo_file")]
    [InlineData("label_list")]
    [InlineData("repo_collaborators")]
    [InlineData("issue_plan_status")]
    [InlineData("pr_checks")]
    [InlineData("pr_status")]
    [InlineData("pr_ready_at")]
    [InlineData("pr_auto_merge_at")]
    [InlineData("workflow_status_check")]
    [InlineData("body_check")]
    [InlineData("closing_issue_labels")]
    [InlineData("run_list")]
    [InlineData("run_view")]
    [InlineData("run_log")]
    [InlineData("tag_view")]
    [InlineData("release_view")]
    [InlineData("release_list")]
    [InlineData("repo_view")]
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
    public void IssuePlanStatusTakesRepoAndIssue()
    {
        JsonElement schema = this.GetTool("issue_plan_status").ProtocolTool.InputSchema;

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
    public void IssueAddSubIssueTakesRepoAndIssueAndSubIssue()
    {
        JsonElement schema = this.GetTool("issue_add_sub_issue").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["issue", "repo", "subIssue"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["issue", "repo", "subIssue"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueAddSubIssueIsAnIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("issue_add_sub_issue");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "issue_add_sub_issue writes the issue");
        Assert.False(
            tool.ProtocolTool.Annotations?.DestructiveHint,
            userMessage: "issue_add_sub_issue only adds a link"
        );
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "issue_add_sub_issue is idempotent");
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
    public void IssueReopenTakesRepoAndIssueAndOptionalComment()
    {
        JsonElement schema = this.GetTool("issue_reopen").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["comment", "issue", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["issue", "repo"], actual: RequiredNames(schema));
    }

    [Theory]
    [InlineData("pr_close")]
    [InlineData("pr_reopen")]
    public void PrStateToolsTakeRepoAndPrAndOptionalComment(string name)
    {
        JsonElement schema = this.GetTool(name).ProtocolTool.InputSchema;

        Assert.Equal(expected: ["comment", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
    }

    [Theory]
    [InlineData("issue_reopen")]
    [InlineData("pr_reopen")]
    public void ReopenToolsAreIdempotentNonDestructiveWrites(string name)
    {
        McpServerTool tool = this.GetTool(name);

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: name + " changes the state");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: name + " is not destructive");
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: name + " is idempotent");
    }

    [Fact]
    public void PrCloseIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_close");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_close writes the pull request");
        Assert.True(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "pr_close is destructive");
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "pr_close is idempotent");
    }

    [Fact]
    public void IssueSearchTakesRepoAndTextAndOptionalStateAndLimit()
    {
        JsonElement schema = this.GetTool("issue_search").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["limit", "repo", "state", "text"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "text"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrSearchTakesRepoAndTextAndOptionalStateAndLimit()
    {
        JsonElement schema = this.GetTool("pr_search").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["limit", "repo", "state", "text"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "text"], actual: RequiredNames(schema));
    }

    [Fact]
    public void CodeSearchTakesRepoAndTextAndOptionalLimit()
    {
        JsonElement schema = this.GetTool("code_search").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["limit", "repo", "text"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "text"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoFileTakesRepoAndPathAndOptionalRef()
    {
        JsonElement schema = this.GetTool("repo_file").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["path", "ref", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["path", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void TagViewTakesRepoAndTag()
    {
        JsonElement schema = this.GetTool("tag_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo", "tag"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "tag"], actual: RequiredNames(schema));
    }

    [Fact]
    public void ReleaseViewTakesRepo()
    {
        JsonElement schema = this.GetTool("release_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void ReleaseListTakesRepo()
    {
        JsonElement schema = this.GetTool("release_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoSearchTakesOwnerAndTextAndOptionalArchivedAndLimit()
    {
        JsonElement schema = this.GetTool("repo_search").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["includeArchived", "limit", "owner", "text"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["owner", "text"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoListTakesOwnerAndOptionalArchived()
    {
        JsonElement schema = this.GetTool("repo_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["includeArchived", "owner"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["owner"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowListTakesRepo()
    {
        JsonElement schema = this.GetTool("workflow_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoDependabotAlertsTakesRepoAndOptionalFilters()
    {
        JsonElement schema = this.GetTool("repo_dependabot_alerts").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["limit", "repo", "severity", "state"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void LabelListTakesRepo()
    {
        JsonElement schema = this.GetTool("label_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoCollaboratorsTakesRepo()
    {
        JsonElement schema = this.GetTool("repo_collaborators").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RepoViewTakesRepo()
    {
        JsonElement schema = this.GetTool("repo_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueListTakesRepo()
    {
        JsonElement schema = this.GetTool("issue_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RunListTakesRepoAndOptionalFilters()
    {
        JsonElement schema = this.GetTool("run_list").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["branch", "limit", "repo", "sha", "workflow"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RunViewTakesRepoAndRun()
    {
        JsonElement schema = this.GetTool("run_view").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo", "run"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "run"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RunLogTakesRepoAndRunAndOptionalJobLinesAndSearch()
    {
        JsonElement schema = this.GetTool("run_log").ProtocolTool.InputSchema;

        Assert.Equal(
            expected: ["context", "fromLine", "grep", "job", "lines", "maxMatches", "repo", "run"],
            actual: PropertyNames(schema)
        );
        Assert.Equal(expected: ["repo", "run"], actual: RequiredNames(schema));
    }

    [Fact]
    public void RunRerunTakesRepoAndRun()
    {
        JsonElement schema = this.GetTool("run_rerun").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["repo", "run"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "run"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowRunTakesRepoAndWorkflowAndOptionalRefAndInputs()
    {
        JsonElement schema = this.GetTool("workflow_run").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["inputs", "ref", "repo", "workflow"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo", "workflow"], actual: RequiredNames(schema));
    }

    [Fact]
    public void WorkflowRunIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("workflow_run");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "workflow_run starts a run");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "workflow_run is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "workflow_run is not idempotent");
    }

    [Fact]
    public void RunRerunIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("run_rerun");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "run_rerun starts a run attempt");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "run_rerun is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "run_rerun is not idempotent");
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
    public void PrEditTakesRepoPrAndOptionalTitleBodyPriorityAndAssignSelf()
    {
        JsonElement schema = this.GetTool("pr_edit").ProtocolTool.InputSchema;

        Assert.Equal(
            expected: ["assignSelf", "body", "pr", "priority", "repo", "title"],
            actual: PropertyNames(schema)
        );
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrEditIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_edit");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_edit writes a pull request");
        Assert.True(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "pr_edit replaces the title and body");
        Assert.True(
            tool.ProtocolTool.Annotations?.IdempotentHint,
            userMessage: "pr_edit repeated changes nothing more"
        );
    }

    [Fact]
    public void IssueEditTakesRepoIssueAndOptionalTitleBodyPriorityAndAssignSelf()
    {
        JsonElement schema = this.GetTool("issue_edit").ProtocolTool.InputSchema;

        Assert.Equal(
            expected: ["assignSelf", "body", "issue", "priority", "repo", "title"],
            actual: PropertyNames(schema)
        );
        Assert.Equal(expected: ["issue", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void IssueEditIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("issue_edit");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "issue_edit writes an issue");
        Assert.True(
            tool.ProtocolTool.Annotations?.DestructiveHint,
            userMessage: "issue_edit replaces the title and body"
        );
        Assert.True(
            tool.ProtocolTool.Annotations?.IdempotentHint,
            userMessage: "issue_edit repeated changes nothing more"
        );
    }

    [Fact]
    public void PrHandOffTakesRepoPrTitleDescriptionAndOptionalRecord()
    {
        JsonElement schema = this.GetTool("pr_hand_off").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["description", "pr", "record", "repo", "title"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["description", "pr", "repo", "title"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrHandOffIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_hand_off");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_hand_off writes a pull request");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "pr_hand_off is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "pr_hand_off is not idempotent");
    }

    [Fact]
    public void PrDiffTakesRepoAndPrAndOptionalMode()
    {
        JsonElement schema = this.GetTool("pr_diff").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["mode", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
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
    public void PrReviewCommentTakesRepoAndPrAndPathAndLinesAndSideAndBody()
    {
        JsonElement schema = this.GetTool("pr_review_comment").ProtocolTool.InputSchema;

        Assert.Equal(
            expected: ["body", "line", "path", "pr", "repo", "side", "startLine"],
            actual: PropertyNames(schema)
        );
        Assert.Equal(expected: ["body", "line", "path", "pr", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrReviewCommentIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("pr_review_comment");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "pr_review_comment writes a comment");
        Assert.False(
            tool.ProtocolTool.Annotations?.DestructiveHint,
            userMessage: "pr_review_comment is not destructive"
        );
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "pr_review_comment is not idempotent");
    }

    [Fact]
    public void CommentAddTakesRepoAndBodyAndOptionalTarget()
    {
        JsonElement schema = this.GetTool("comment_add").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["body", "issue", "pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["body", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void CommentAddIsANonIdempotentNonDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("comment_add");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "comment_add writes a comment");
        Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint, userMessage: "comment_add is not destructive");
        Assert.False(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "comment_add is not idempotent");
    }

    [Fact]
    public void PrChecksTakesRepoAndOptionalTargets()
    {
        JsonElement schema = this.GetTool("pr_checks").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["all", "branch", "pr", "repo", "sha"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void ClosingIssueLabelsTakesRepoAndPr()
    {
        JsonElement schema = this.GetTool("closing_issue_labels").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrReadyAtTakesRepoAndPr()
    {
        JsonElement schema = this.GetTool("pr_ready_at").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void PrAutoMergeAtTakesRepoAndPr()
    {
        JsonElement schema = this.GetTool("pr_auto_merge_at").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["pr", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["pr", "repo"], actual: RequiredNames(schema));
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

    [Fact]
    public void LabelCreateTakesRepoNameDescriptionAndColor()
    {
        JsonElement schema = this.GetTool("label_create").ProtocolTool.InputSchema;

        Assert.Equal(expected: ["color", "description", "name", "repo"], actual: PropertyNames(schema));
        Assert.Equal(expected: ["color", "description", "name", "repo"], actual: RequiredNames(schema));
    }

    [Fact]
    public void LabelCreateIsAnIdempotentDestructiveWrite()
    {
        McpServerTool tool = this.GetTool("label_create");

        Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint, userMessage: "label_create writes labels");
        Assert.True(
            tool.ProtocolTool.Annotations?.DestructiveHint,
            userMessage: "label_create can overwrite an existing label"
        );
        Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint, userMessage: "label_create is idempotent");
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
            .AddMockedService<ILabelListService>()
            .AddMockedService<IIssueService>()
            .AddMockedService<IIssueCreateService>()
            .AddMockedService<IIssueEditService>()
            .AddMockedService<IIssueSearchService>()
            .AddMockedService<IPrSearchService>()
            .AddMockedService<ICodeSearchService>()
            .AddMockedService<IRepoSearchService>()
            .AddMockedService<IRepoFileService>()
            .AddMockedService<ITagViewService>()
            .AddMockedService<IReleaseService>()
            .AddMockedService<IIssueListService>()
            .AddMockedService<IRepositoryListService>()
            .AddMockedService<IRepoCollaboratorsService>()
            .AddMockedService<IRepoViewService>()
            .AddMockedService<IWorkflowListService>()
            .AddMockedService<IDependabotAlertsService>()
            .AddMockedService<IRunListService>()
            .AddMockedService<IRunViewService>()
            .AddMockedService<IRunLogService>()
            .AddMockedService<IRunRerunService>()
            .AddMockedService<IWorkflowRunService>()
            .AddMockedService<IIssuePlanStatusReader>()
            .AddMockedService<IPrCreateService>()
            .AddMockedService<IPrEditService>()
            .AddMockedService<IPrHandOffService>()
            .AddMockedService<IPrReplyService>()
            .AddMockedService<IPrReviewCommentService>()
            .AddMockedService<IPrDiffService>()
            .AddMockedService<ICommentAddService>()
            .AddMockedService<IBodyCheckService>()
            .AddMockedService<IPrChecksService>()
            .AddMockedService<IClosingIssueLabelsService>()
            .AddMockedService<IWorkflowStatusCheckService>()
            .AddMockedService<IWorkflowStatusSetService>()
            .AddMockedService<IPrStatusService>()
            .AddMockedService<IPrReadyAtService>()
            .AddMockedService<IPrAutoMergeAtService>()
            .AddMcpTools();
    }
}
