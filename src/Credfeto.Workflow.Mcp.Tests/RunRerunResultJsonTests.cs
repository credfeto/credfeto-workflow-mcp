using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RunRerunResultJsonTests : TestBase
{
    [Fact]
    public static void AStartedRerunHoldsTheAttempt()
    {
        AssertRoundTrips(
            result: new(
                Outcome: RunRerunOutcome.Success,
                Message: "Rerunning failed jobs of run 5 (attempt 3)",
                Run: 5,
                Attempt: 3,
                Rule: null
            ),
            json: "{\"outcome\":\"Success\",\"message\":\"Rerunning failed jobs of run 5 (attempt 3)\",\"run\":5,\"attempt\":3}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRule()
    {
        AssertRoundTrips(
            result: new(
                Outcome: RunRerunOutcome.Refused,
                Message: "rerun refused: attempt limit",
                Run: 5,
                Attempt: null,
                Rule: "attempt limit"
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"rerun refused: attempt limit\",\"run\":5,\"rule\":\"attempt limit\"}"
        );
    }

    [Theory]
    [InlineData(RunRerunOutcome.Pending, "Pending")]
    [InlineData(RunRerunOutcome.NotFound, "NotFound")]
    public static void AnOutcomeWithoutAnAttemptOrRuleHoldsOnlyTheOutcomeMessageAndRun(
        RunRerunOutcome outcome,
        string name
    )
    {
        AssertRoundTrips(
            result: new(Outcome: outcome, Message: "the run 5", Run: 5, Attempt: null, Rule: null),
            json: "{\"outcome\":\"" + name + "\",\"message\":\"the run 5\",\"run\":5}"
        );
    }

    private static void AssertRoundTrips(RunRerunResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RunRerunResult);

        Assert.Equal(expected: json, actual: actual);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.RunRerunResult)
        );
    }
}
