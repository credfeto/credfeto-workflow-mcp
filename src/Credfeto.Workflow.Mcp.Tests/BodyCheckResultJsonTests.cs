using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class BodyCheckResultJsonTests : TestBase
{
    [Fact]
    public static void APassHoldsOnlyTheOutcome()
    {
        AssertRoundTrips(
            result: new(Outcome: BodyCheckOutcome.Passed, Rule: null, Line: null, Message: null),
            json: "{\"outcome\":\"Passed\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleLineAndMessage()
    {
        AssertRoundTrips(
            result: new(
                Outcome: BodyCheckOutcome.Refused,
                Rule: "token",
                Line: 2,
                Message: "body refused: token (line 2)"
            ),
            json: "{\"outcome\":\"Refused\",\"rule\":\"token\",\"line\":2,\"message\":\"body refused: token (line 2)\"}"
        );
    }

    [Fact]
    public static void ARefusalOfTheWholeBodyHasNoLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: BodyCheckOutcome.Refused,
                Rule: "too long",
                Line: null,
                Message: "body refused: too long"
            ),
            json: "{\"outcome\":\"Refused\",\"rule\":\"too long\",\"message\":\"body refused: too long\"}"
        );
    }

    private static void AssertRoundTrips(BodyCheckResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.BodyCheckResult);

        Assert.Equal(expected: json, actual: actual);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.BodyCheckResult)
        );
    }
}
