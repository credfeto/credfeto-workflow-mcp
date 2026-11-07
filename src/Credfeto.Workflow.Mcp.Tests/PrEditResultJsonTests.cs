using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrEditResultJsonTests : TestBase
{
    [Fact]
    public static void AnEditedPullRequestHoldsItsUrl()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrEditOutcome.Edited,
                Message: "credfeto/credfeto-workflow-mcp#87 was edited",
                Url: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87"),
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"Edited\",\"message\":\"credfeto/credfeto-workflow-mcp#87 was edited\",\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/87\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrEditOutcome.Refused,
                Message: "body refused: token (line 3)",
                Url: null,
                Rule: "token",
                LineNumber: 3
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"body refused: token (line 3)\",\"rule\":\"token\",\"lineNumber\":3}"
        );
    }

    [Fact]
    public static void AFailedEditHoldsOnlyTheOutcomeAndMessage()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrEditOutcome.MayHaveBeenEdited,
                Message: "may have been edited",
                Url: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"MayHaveBeenEdited\",\"message\":\"may have been edited\"}"
        );
    }

    private static void AssertRoundTrips(PrEditResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.PrEditResult);

        Assert.Equal(expected: json, actual: actual);

        PrEditResult? read = JsonSerializer.Deserialize(
            json: actual,
            jsonTypeInfo: AppJsonContext.Default.PrEditResult
        );

        Assert.Equal(expected: result, actual: read);
    }
}
