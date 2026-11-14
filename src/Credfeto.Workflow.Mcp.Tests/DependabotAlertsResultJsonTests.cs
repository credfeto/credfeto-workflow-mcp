using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class DependabotAlertsResultJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"alerts\":[{\"number\":7,\"severity\":\"high\",\"state\":\"open\",\"ecosystem\":\"nuget\",\"package\":\"Newtonsoft.Json\",\"manifestPath\":\"src/App/App.csproj\",\"vulnerableRange\":\"= 12.0.3\",\"firstPatchedVersion\":\"13.0.1\",\"ghsaId\":\"GHSA-5crp-9r3c-p9vr\"}]}";

    [Fact]
    public void AResultSerialises()
    {
        DependabotAlertsResult result = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Alerts:
            [
                new DependabotAlertEntry(
                    Number: 7,
                    Severity: "high",
                    State: "open",
                    Ecosystem: "nuget",
                    Package: "Newtonsoft.Json",
                    ManifestPath: "src/App/App.csproj",
                    VulnerableRange: "= 12.0.3",
                    FirstPatchedVersion: "13.0.1",
                    GhsaId: "GHSA-5crp-9r3c-p9vr"
                ),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.DependabotAlertsResult
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        DependabotAlertsResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.DependabotAlertsResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.DependabotAlertsResult)
        );
    }
}
