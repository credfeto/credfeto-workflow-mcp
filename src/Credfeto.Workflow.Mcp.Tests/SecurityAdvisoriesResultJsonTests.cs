using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class SecurityAdvisoriesResultJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"advisories\":[{\"ghsaId\":\"GHSA-5crp-9r3c-p9vr\",\"severity\":\"high\",\"state\":\"published\",\"summary\":\"A summary\"}]}";

    [Fact]
    public void AResultSerialises()
    {
        SecurityAdvisoriesResult result = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Advisories:
            [
                new SecurityAdvisoryEntry(
                    GhsaId: "GHSA-5crp-9r3c-p9vr",
                    Severity: "high",
                    State: "published",
                    Summary: "A summary"
                ),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.SecurityAdvisoriesResult
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        SecurityAdvisoriesResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.SecurityAdvisoriesResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(
                value: result,
                jsonTypeInfo: AppJsonContext.Default.SecurityAdvisoriesResult
            )
        );
    }
}
