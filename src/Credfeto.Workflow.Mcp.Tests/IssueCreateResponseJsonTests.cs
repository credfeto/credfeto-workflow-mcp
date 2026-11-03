using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueCreateResponseJsonTests : TestBase
{
    [Fact]
    public void AResponseRoundTrips()
    {
        IssueCreateResponse response = new(
            new Uri(
                uriString: "https://github.com/credfeto/credfeto-workflow-mcp/issues/90",
                uriKind: UriKind.Absolute
            )
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse
        );

        Assert.Equal(
            expected: "{\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/90\"}",
            actual: json
        );
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(json: json, jsonTypeInfo: AppJsonContext.Default.IssueCreateResponse)
        );
    }
}
