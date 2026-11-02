using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowStatusCheckResultJsonTests : TestBase
{
    [Fact]
    public void AFoundResultRoundTrips()
    {
        WorkflowStatusCheckResult result = new(
            Outcome: WorkflowStatusCheckOutcome.Found,
            State: "Development",
            Message: null
        );

        string json = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.WorkflowStatusCheckResult
        );

        Assert.Equal(expected: "{\"outcome\":\"Found\",\"state\":\"Development\"}", actual: json);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(
                json: json,
                jsonTypeInfo: AppJsonContext.Default.WorkflowStatusCheckResult
            )
        );
    }

    [Fact]
    public void ANotOnBoardResultRoundTrips()
    {
        WorkflowStatusCheckResult result = new(
            Outcome: WorkflowStatusCheckOutcome.NotOnBoard,
            State: null,
            Message: "credfeto/credfeto-workflow-mcp#81 is not on the Workflow board"
        );

        string json = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.WorkflowStatusCheckResult
        );

        Assert.Equal(
            expected: "{\"outcome\":\"NotOnBoard\",\"message\":\"credfeto/credfeto-workflow-mcp#81 is not on the Workflow board\"}",
            actual: json
        );
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(
                json: json,
                jsonTypeInfo: AppJsonContext.Default.WorkflowStatusCheckResult
            )
        );
    }

    [Fact]
    public void AnUnsetResultWritesOnlyTheOutcome()
    {
        WorkflowStatusCheckResult result = new(Outcome: WorkflowStatusCheckOutcome.Unset, State: null, Message: null);

        string json = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.WorkflowStatusCheckResult
        );

        Assert.Equal(expected: "{\"outcome\":\"Unset\"}", actual: json);
    }
}
