using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueEditResultJsonTests : TestBase
{
    [Fact]
    public static void AnEditedIssueHoldsItsUrl()
    {
        AssertRoundTrips(
            result: new(
                Outcome: IssueEditOutcome.Edited,
                Message: "credfeto/credfeto-workflow-mcp#47 was edited",
                Url: new Uri("https://github.com/credfeto/credfeto-workflow-mcp/issues/47"),
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"Edited\",\"message\":\"credfeto/credfeto-workflow-mcp#47 was edited\",\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/issues/47\"}"
        );
    }

    [Fact]
    public static void ARefusalHoldsTheRuleAndLine()
    {
        AssertRoundTrips(
            result: new(
                Outcome: IssueEditOutcome.Refused,
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
                Outcome: IssueEditOutcome.Failed,
                Message: "was not edited",
                Url: null,
                Rule: null,
                LineNumber: null
            ),
            json: "{\"outcome\":\"Failed\",\"message\":\"was not edited\"}"
        );
    }

    private static void AssertRoundTrips(IssueEditResult result, string json)
    {
        string actual = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.IssueEditResult);

        Assert.Equal(expected: json, actual: actual);

        IssueEditResult? read = JsonSerializer.Deserialize(
            json: actual,
            jsonTypeInfo: AppJsonContext.Default.IssueEditResult
        );

        Assert.Equal(expected: result, actual: read);
    }
}
