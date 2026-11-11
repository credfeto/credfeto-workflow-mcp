using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrAutoMergeAtResponseJsonTests : TestBase
{
    [Theory]
    [InlineData("2026-01-02T03:04:05Z", "{\"pr\":62,\"enabledAt\":\"2026-01-02T03:04:05Z\"}")]
    [InlineData("-", "{\"pr\":62,\"enabledAt\":\"-\"}")]
    public static void AResponseSerialises(string enabledAt, string expected)
    {
        PrAutoMergeAtResponse response = new(Pr: 62, EnabledAt: enabledAt);

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.PrAutoMergeAtResponse
        );

        Assert.Equal(expected: expected, actual: json);
    }

    [Fact]
    public static void AResponseRoundTrips()
    {
        PrAutoMergeAtResponse? response = JsonSerializer.Deserialize(
            json: "{\"pr\":62,\"enabledAt\":\"2026-01-02T03:04:05Z\"}",
            jsonTypeInfo: AppJsonContext.Default.PrAutoMergeAtResponse
        );

        Assert.Equal(expected: new(Pr: 62, EnabledAt: "2026-01-02T03:04:05Z"), actual: response);
    }
}
