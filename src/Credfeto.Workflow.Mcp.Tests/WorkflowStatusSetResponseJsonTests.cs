using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowStatusSetResponseJsonTests : TestBase
{
    [Fact]
    public void AResponseRoundTrips()
    {
        WorkflowStatusSetResponse response = new(
            ItemUrl: new(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/pull/82",
                uriKind: UriKind.Absolute
            ),
            Status: "AI Review"
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.WorkflowStatusSetResponse
        );

        Assert.Equal(
            expected: "{\"itemUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/82\",\"status\":\"AI Review\"}",
            actual: json
        );
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(
                json: json,
                jsonTypeInfo: AppJsonContext.Default.WorkflowStatusSetResponse
            )
        );
    }
}
