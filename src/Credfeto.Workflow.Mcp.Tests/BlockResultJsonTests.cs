using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class BlockResultJsonTests : TestBase
{
    [Fact]
    public static void ABlockHoldsTheOutcomeMessageUrlAndWhetherItWasAlreadyBlocked()
    {
        AssertRoundTrips(
            result: new(
                Outcome: BlockOutcome.Blocked,
                Message: "the comment was posted on credfeto/credfeto-workflow-mcp#174, which was already Blocked",
                CommentUrl: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1"),
                AlreadyBlocked: true,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"Blocked\",\"message\":\"the comment was posted on credfeto/credfeto-workflow-mcp#174, which was already Blocked\",\"commentUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1\",\"alreadyBlocked\":true}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: BlockOutcome.Refused,
                Message: "body refused: malformed env-block marker (line 2)",
                CommentUrl: null,
                AlreadyBlocked: false,
                Rule: "malformed env-block marker",
                LineNumber: 2
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"body refused: malformed env-block marker (line 2)\",\"alreadyBlocked\":false,\"rule\":\"malformed env-block marker\",\"lineNumber\":2}"
        );
    }

    [Fact]
    public static void ALabelFailureHoldsTheCommentUrl()
    {
        AssertRoundTrips(
            result: new(
                Outcome: BlockOutcome.LabelNotAdded,
                Message: "could not add the Blocked label to credfeto/credfeto-workflow-mcp#174; steps done: comment posted https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1",
                CommentUrl: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1"),
                AlreadyBlocked: false,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"LabelNotAdded\",\"message\":\"could not add the Blocked label to credfeto/credfeto-workflow-mcp#174; steps done: comment posted https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1\",\"commentUrl\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/174#issuecomment-1\",\"alreadyBlocked\":false}"
        );
    }

    private static void AssertRoundTrips(BlockResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.BlockResult);

        Assert.Equal(expected: json, actual: actual);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.BlockResult)
        );
    }
}
