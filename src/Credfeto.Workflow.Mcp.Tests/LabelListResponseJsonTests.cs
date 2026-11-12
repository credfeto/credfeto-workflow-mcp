using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class LabelListResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"labels\":[\"bug\",\"Priority 1\"]}";

    [Fact]
    public void AResponseSerialises()
    {
        LabelListResponse response = new(Repository: "credfeto/credfeto-workflow-mcp", Labels: ["bug", "Priority 1"]);

        string json = JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.LabelListResponse);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        LabelListResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.LabelListResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.LabelListResponse)
        );
    }
}
