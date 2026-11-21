using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class AdvisoryViewResultJsonTests : TestBase
{
    private const string GHSA = "GHSA-jfh8-c2jp-5v3q";

    private const string JSON =
        "{\"ghsaId\":\""
        + GHSA
        + "\",\"severity\":\"high\",\"summary\":\"Remote code injection\",\"published\":\"2021-12-10T00:40:56Z\",\"withdrawn\":\"\",\"packages\":[{\"ecosystem\":\"nuget\",\"package\":\"Example.Package\",\"vulnerableRange\":\"= 1.2.2\",\"firstPatched\":\"\"}]}";

    private const string NO_PACKAGES_JSON =
        "{\"ghsaId\":\""
        + GHSA
        + "\",\"severity\":\"low\",\"summary\":\"\",\"published\":\"2021-12-10T00:40:56Z\",\"withdrawn\":\"2022-01-05T12:00:00Z\",\"packages\":[]}";

    [Fact]
    public static void AnAdvisorySerialisesInCamelCase()
    {
        AdvisoryViewResult result = new(
            GhsaId: GHSA,
            Severity: "high",
            Summary: "Remote code injection",
            Published: "2021-12-10T00:40:56Z",
            Withdrawn: string.Empty,
            Packages:
            [
                new(
                    Ecosystem: "nuget",
                    Package: "Example.Package",
                    VulnerableRange: "= 1.2.2",
                    FirstPatched: string.Empty
                ),
            ]
        );

        Assert.Equal(expected: JSON, actual: Serialise(result));
    }

    [Theory]
    [InlineData(JSON)]
    [InlineData(NO_PACKAGES_JSON)]
    public static void AnAdvisoryRoundTrips(string json)
    {
        AdvisoryViewResult? result = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.AdvisoryViewResult
        );

        Assert.NotNull(result);
        Assert.Equal(expected: json, actual: Serialise(result));
    }

    private static string Serialise(AdvisoryViewResult result)
    {
        return JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.AdvisoryViewResult);
    }
}
