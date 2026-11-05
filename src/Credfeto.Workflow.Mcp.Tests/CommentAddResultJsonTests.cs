using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CommentAddResultJsonTests : TestBase
{
    [Fact]
    public static void APostedCommentHoldsTheOutcomeMessageAndUrl()
    {
        AssertRoundTrips(
            result: new(
                Outcome: CommentAddOutcome.Posted,
                Message: "the comment was posted on credfeto/credfeto-workflow-mcp#31",
                Url: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-99"),
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"Posted\",\"message\":\"the comment was posted on credfeto/credfeto-workflow-mcp#31\",\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/31#issuecomment-99\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: CommentAddOutcome.Refused,
                Message: "body refused: approval keyword (line 3)",
                Url: null,
                Rule: "approval keyword",
                LineNumber: 3
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"body refused: approval keyword (line 3)\",\"rule\":\"approval keyword\",\"lineNumber\":3}"
        );
    }

    [Fact]
    public static void AFailedPostHoldsOnlyTheOutcomeAndMessage()
    {
        AssertRoundTrips(
            result: new(
                Outcome: CommentAddOutcome.MayHaveBeenPosted,
                Message: "unexpected result for the comment, which may have been posted on credfeto/credfeto-workflow-mcp#31",
                Url: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"MayHaveBeenPosted\",\"message\":\"unexpected result for the comment, which may have been posted on credfeto/credfeto-workflow-mcp#31\"}"
        );
    }

    private static void AssertRoundTrips(CommentAddResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.CommentAddResult);

        Assert.Equal(expected: json, actual: actual);
        Assert.Equal(
            expected: result,
            actual: JsonSerializer.Deserialize(json: actual, jsonTypeInfo: AppJsonContext.Default.CommentAddResult)
        );
    }
}
