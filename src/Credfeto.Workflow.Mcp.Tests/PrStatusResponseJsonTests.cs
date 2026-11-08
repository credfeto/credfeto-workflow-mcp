using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrStatusResponseJsonTests : TestBase
{
    private static PrStatusResponse Create(string? board, PrStatusLastActivity? last)
    {
        return new(
            Pr: "credfeto/credfeto-workflow-mcp#73",
            State: "OPEN",
            Draft: "no",
            Title: "Add the status tool",
            Url: new("https://github.com/credfeto/credfeto-workflow-mcp/pull/73"),
            Base: "main",
            Head: "feat/73-pr-status-tool",
            HeadSha: "0123456789abcdef0123456789abcdef01234567",
            AutoMerge: "off",
            ReviewDecision: "REVIEW_REQUIRED",
            MergeState: "BLOCKED",
            Mergeable: "MERGEABLE",
            Labels: "enhancement",
            Assignees: "credfeto",
            Author: "credfeto",
            ChangedFiles: 12,
            Board: board,
            ReadyAt: "2026-01-02T03:04:05Z",
            ReadySource: "ready-for-review",
            Checks: new(Verdict: PrChecksVerdict.Failed, Failing: ["build"], Pending: ["lint"]),
            Activity: new(Comments: 3, Reviews: 1, Threads: 2, Unresolved: 1, SinceReady: 2, Last: last)
        );
    }

    [Fact]
    public void AFullStatusSerialisesEveryField()
    {
        PrStatusResponse response = Create(
            board: "AI Review",
            last: new(At: "2026-01-03T00:00:00Z", Author: "steward-warden")
        );

        string json = JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PrStatusResponse);

        Assert.Equal(
            expected: "{\"pr\":\"credfeto/credfeto-workflow-mcp#73\",\"state\":\"OPEN\",\"draft\":\"no\",\"title\":\"Add the status tool\",\"url\":\"https://github.com/credfeto/credfeto-workflow-mcp/pull/73\",\"base\":\"main\",\"head\":\"feat/73-pr-status-tool\",\"headSha\":\"0123456789abcdef0123456789abcdef01234567\",\"autoMerge\":\"off\",\"reviewDecision\":\"REVIEW_REQUIRED\",\"mergeState\":\"BLOCKED\",\"mergeable\":\"MERGEABLE\",\"labels\":\"enhancement\",\"assignees\":\"credfeto\",\"author\":\"credfeto\",\"changedFiles\":12,\"board\":\"AI Review\",\"readyAt\":\"2026-01-02T03:04:05Z\",\"readySource\":\"ready-for-review\",\"checks\":{\"verdict\":\"Failed\",\"failing\":[\"build\"],\"pending\":[\"lint\"]},\"activity\":{\"comments\":3,\"reviews\":1,\"threads\":2,\"unresolved\":1,\"sinceReady\":2,\"last\":{\"at\":\"2026-01-03T00:00:00Z\",\"author\":\"steward-warden\"}}}",
            actual: json
        );

        PrStatusResponse? read = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.PrStatusResponse
        );

        Assert.NotNull(read);
        Assert.Equal(expected: response.Activity, actual: read.Activity);
        Assert.Equal(expected: response.Checks.Failing, actual: read.Checks.Failing);
        Assert.Equal(expected: response.Checks.Pending, actual: read.Checks.Pending);
        Assert.Equal(expected: response with { Checks = read.Checks }, actual: read);
    }

    [Fact]
    public void NoBoardAndNoActivityAreLeftOut()
    {
        PrStatusResponse response = Create(board: null, last: null);

        string json = JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PrStatusResponse);

        Assert.DoesNotContain(
            expectedSubstring: "\"board\"",
            actualString: json,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "\"last\"",
            actualString: json,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "\"activity\":{\"comments\":3,\"reviews\":1,\"threads\":2,\"unresolved\":1,\"sinceReady\":2}",
            actualString: json,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public void AReadyAtResponseRoundTrips()
    {
        PrReadyAtResponse response = new(ReadyAt: "2026-01-02T03:04:05Z", Source: "created");

        string json = JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PrReadyAtResponse);

        Assert.Equal(expected: "{\"readyAt\":\"2026-01-02T03:04:05Z\",\"source\":\"created\"}", actual: json);
        Assert.Equal(
            expected: response,
            actual: JsonSerializer.Deserialize(json: json, jsonTypeInfo: AppJsonContext.Default.PrReadyAtResponse)
        );
    }
}
