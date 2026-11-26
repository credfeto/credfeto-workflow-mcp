using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrTakeBackResultJsonTests : TestBase
{
    [Fact]
    public static void ATakenBackPullRequestHoldsTheCommentAndWhatItWasBefore()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrTakeBackOutcome.TakenBack,
                Message: "credfeto/credfeto-workflow-mcp#87 was taken back: auto-merge off, draft, Development",
                CommentUrl: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99"),
                AutoMergeWasOn: true,
                AlreadyDraft: false,
                StepsDone: ["auto-merge off", "draft", "Workflow board set to Development", "comment posted"],
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"TakenBack\",\"message\":\"credfeto/credfeto-workflow-mcp#87 was taken back: auto-merge off, draft, Development\",\"commentUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99\",\"autoMergeWasOn\":true,\"alreadyDraft\":false,\"stepsDone\":[\"auto-merge off\",\"draft\",\"Workflow board set to Development\",\"comment posted\"]}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrTakeBackOutcome.Refused,
                Message: "body refused: approval keyword (line 3)",
                CommentUrl: null,
                AutoMergeWasOn: null,
                AlreadyDraft: null,
                StepsDone: null,
                Rule: "approval keyword",
                LineNumber: 3
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"body refused: approval keyword (line 3)\",\"rule\":\"approval keyword\",\"lineNumber\":3}"
        );
    }

    [Fact]
    public static void AFailedTakeBackHoldsTheStepsDone()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrTakeBackOutcome.MayHaveBeenTakenBack,
                Message: "may have been taken back",
                CommentUrl: null,
                AutoMergeWasOn: null,
                AlreadyDraft: null,
                StepsDone: ["auto-merge off"],
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"MayHaveBeenTakenBack\",\"message\":\"may have been taken back\",\"stepsDone\":[\"auto-merge off\"]}"
        );
    }

    [Theory]
    [InlineData(PrTakeBackOutcome.NotFound, "NotFound")]
    [InlineData(PrTakeBackOutcome.NotTakenBack, "NotTakenBack")]
    public static void TheOutcomeIsWrittenByName(PrTakeBackOutcome outcome, string name)
    {
        AssertRoundTrips(
            result: new(
                Outcome: outcome,
                Message: "m",
                CommentUrl: null,
                AutoMergeWasOn: null,
                AlreadyDraft: null,
                StepsDone: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"" + name + "\",\"message\":\"m\"}"
        );
    }

    // The record compares its list by reference, so the round trip is checked through the JSON it gives again
    private static void AssertRoundTrips(PrTakeBackResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.PrTakeBackResult);

        Assert.Equal(expected: json, actual: actual);

        PrTakeBackResult? read = JsonSerializer.Deserialize(
            json: actual,
            jsonTypeInfo: AppJsonContext.Default.PrTakeBackResult
        );

        Assert.NotNull(read);
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(value: read, jsonTypeInfo: AppJsonContext.Default.PrTakeBackResult)
        );
    }
}
