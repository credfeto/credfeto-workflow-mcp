using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrHandOffResultJsonTests : TestBase
{
    private const string HEAD_SHA = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public static void AHandedOffPullRequestHoldsTheHeadRecordAndAutoMerge()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrHandOffOutcome.HandedOff,
                Message: "credfeto/credfeto-workflow-mcp#87 was handed off for human review; auto-merge on",
                HeadSha: HEAD_SHA,
                ReviewRecordUrl: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99"),
                AutoMerge: PrHandOffAutoMerge.On,
                AutoMergeReason: null,
                StepsDone: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"HandedOff\",\"message\":\"credfeto/credfeto-workflow-mcp#87 was handed off for human review; auto-merge on\",\"headSha\":\""
                + HEAD_SHA
                + "\",\"reviewRecordUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99\",\"autoMerge\":\"On\"}"
        );
    }

    [Fact]
    public static void AHandOffWithoutAutoMergeHoldsTheReason()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrHandOffOutcome.HandedOff,
                Message: "handed off",
                HeadSha: HEAD_SHA,
                ReviewRecordUrl: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99"),
                AutoMerge: PrHandOffAutoMerge.NotArmed,
                AutoMergeReason: "the base branch requires no check and no review",
                StepsDone: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"HandedOff\",\"message\":\"handed off\",\"headSha\":\""
                + HEAD_SHA
                + "\",\"reviewRecordUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/87#issuecomment-99\",\"autoMerge\":\"NotArmed\",\"autoMergeReason\":\"the base branch requires no check and no review\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrHandOffOutcome.Refused,
                Message: "description refused: approval keyword (line 3)",
                HeadSha: null,
                ReviewRecordUrl: null,
                AutoMerge: null,
                AutoMergeReason: null,
                StepsDone: null,
                Rule: "approval keyword",
                LineNumber: 3
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"description refused: approval keyword (line 3)\",\"rule\":\"approval keyword\",\"lineNumber\":3}"
        );
    }

    [Fact]
    public static void AFailedHandOffHoldsTheStepsDone()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrHandOffOutcome.NotHandedOff,
                Message: "not handed off",
                HeadSha: null,
                ReviewRecordUrl: null,
                AutoMerge: null,
                AutoMergeReason: null,
                StepsDone: ["title and description set", "Workflow board set to Human Review"],
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"NotHandedOff\",\"message\":\"not handed off\",\"stepsDone\":[\"title and description set\",\"Workflow board set to Human Review\"]}"
        );
    }

    // The record compares its list by reference, so the round trip is checked through the JSON it gives again
    private static void AssertRoundTrips(PrHandOffResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.PrHandOffResult);

        Assert.Equal(expected: json, actual: actual);

        PrHandOffResult? read = JsonSerializer.Deserialize(
            json: actual,
            jsonTypeInfo: AppJsonContext.Default.PrHandOffResult
        );

        Assert.NotNull(read);
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(value: read, jsonTypeInfo: AppJsonContext.Default.PrHandOffResult)
        );
    }
}
