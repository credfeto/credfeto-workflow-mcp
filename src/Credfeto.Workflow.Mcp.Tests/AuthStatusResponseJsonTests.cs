using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class AuthStatusResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"login\":\"credfeto\",\"host\":\"mcp.example\",\"version\":\"2026-03-10\",\"rateLimits\":{\"core\":{\"remaining\":4990,\"limit\":5000,\"reset\":\"2026-11-19T10:00:00Z\"},\"graphql\":{\"remaining\":4000,\"limit\":5000,\"reset\":\"2026-11-19T11:00:00Z\"}}}";

    private const string NO_RATE_LIMITS_JSON = "{\"login\":\"credfeto\",\"host\":\"\",\"version\":\"\"}";

    [Fact]
    public static void AResultSerialises()
    {
        AuthStatusResponse result = new(
            Login: "credfeto",
            Host: "mcp.example",
            Version: "2026-03-10",
            RateLimits: new(
                Core: new(Remaining: 4990, Limit: 5000, Reset: "2026-11-19T10:00:00Z"),
                Graphql: new(Remaining: 4000, Limit: 5000, Reset: "2026-11-19T11:00:00Z")
            )
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.AuthStatusResponse);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public static void RateLimitsAreLeftOutWhenNotRead()
    {
        AuthStatusResponse result = new(Login: "credfeto", Host: string.Empty, Version: string.Empty, RateLimits: null);

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.AuthStatusResponse);

        Assert.Equal(expected: NO_RATE_LIMITS_JSON, actual: json);
    }

    [Theory]
    [InlineData(JSON)]
    [InlineData(NO_RATE_LIMITS_JSON)]
    public static void AResultRoundTrips(string json)
    {
        AuthStatusResponse? result = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.AuthStatusResponse
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.AuthStatusResponse)
        );
    }
}
