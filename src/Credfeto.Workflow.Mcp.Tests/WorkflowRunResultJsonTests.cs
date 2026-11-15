using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowRunResultJsonTests : TestBase
{
    [Fact]
    public static void AVisibleRunHoldsItsIdAndUrl()
    {
        AssertRoundTrips(
            result: new(
                Outcome: WorkflowRunOutcome.Success,
                Message: "dispatched; newest run on main is 5",
                Workflow: "deploy.yml",
                Ref: "main",
                Run: 5,
                Url: new("https://github.com/credfeto/credfeto-workflow-mcp/actions/runs/5"),
                Rule: null
            ),
            json: "{\"outcome\":\"Success\",\"message\":\"dispatched; newest run on main is 5\",\"workflow\":\"deploy.yml\",\"ref\":\"main\",\"run\":5,\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/actions/runs/5\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRule()
    {
        AssertRoundTrips(
            result: new(
                Outcome: WorkflowRunOutcome.Refused,
                Message: "workflow run refused: no allowlist",
                Workflow: "deploy.yml",
                Ref: "main",
                Run: null,
                Url: null,
                Rule: "no allowlist"
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"workflow run refused: no allowlist\",\"workflow\":\"deploy.yml\",\"ref\":\"main\",\"rule\":\"no allowlist\"}"
        );
    }

    [Theory]
    [InlineData(WorkflowRunOutcome.NotFound, "NotFound")]
    [InlineData(WorkflowRunOutcome.InvalidInput, "InvalidInput")]
    public static void AnOutcomeWithoutARunOrRuleHoldsOnlyTheOutcomeMessageAndWorkflow(
        WorkflowRunOutcome outcome,
        string name
    )
    {
        AssertRoundTrips(
            result: new(
                Outcome: outcome,
                Message: "the workflow",
                Workflow: "deploy.yml",
                Ref: null,
                Run: null,
                Url: null,
                Rule: null
            ),
            json: "{\"outcome\":\"" + name + "\",\"message\":\"the workflow\",\"workflow\":\"deploy.yml\"}"
        );
    }

    private static void AssertRoundTrips(WorkflowRunResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.WorkflowRunResult);

        Assert.Equal(expected: json, actual: actual);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.WorkflowRunResult)
        );
    }
}
