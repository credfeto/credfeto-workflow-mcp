using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrSyncLabelsResultJsonTests : TestBase
{
    private const string URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/87";

    [Fact]
    public static void ASyncedPullRequestHoldsTheLabelsAddedAndSkipped()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrSyncLabelsOutcome.Synced,
                Message: "credfeto/credfeto-workflow-mcp#87 synced",
                Url: new Uri(URL),
                Added: ["bug", "Urgent"],
                Skipped: ["only-elsewhere"],
                FailedReferences: [],
                Assigned: true,
                Rule: null
            ),
            json: "{\"outcome\":\"Synced\",\"message\":\"credfeto/credfeto-workflow-mcp#87 synced\",\"url\":\""
                + URL
                + "\",\"added\":[\"bug\",\"Urgent\"],\"skipped\":[\"only-elsewhere\"],\"failedReferences\":[],\"assigned\":true}"
        );
    }

    [Fact]
    public static void ARefusedSyncHoldsTheRule()
    {
        AssertRoundTrips(
            result: new(
                Outcome: PrSyncLabelsOutcome.Refused,
                Message: "assignSelf refused: already assigned to another user; nothing was written",
                Url: new Uri(URL),
                Added: [],
                Skipped: [],
                FailedReferences: ["other/repo#3"],
                Assigned: false,
                Rule: "already assigned to another user"
            ),
            json: "{\"outcome\":\"Refused\",\"message\":\"assignSelf refused: already assigned to another user; nothing was written\",\"url\":\""
                + URL
                + "\",\"added\":[],\"skipped\":[],\"failedReferences\":[\"other/repo#3\"],\"assigned\":false,\"rule\":\"already assigned to another user\"}"
        );
    }

    private static void AssertRoundTrips(PrSyncLabelsResult result, string json)
    {
        string actual = JsonSerializer.Serialize(
            value: result,
            jsonTypeInfo: AppJsonContext.Default.PrSyncLabelsResult
        );

        Assert.Equal(expected: json, actual: actual);

        PrSyncLabelsResult? read = JsonSerializer.Deserialize(
            json: actual,
            jsonTypeInfo: AppJsonContext.Default.PrSyncLabelsResult
        );

        Assert.NotNull(read);
        Assert.Equal(
            expected: actual,
            actual: JsonSerializer.Serialize(value: read, jsonTypeInfo: AppJsonContext.Default.PrSyncLabelsResult)
        );
    }
}
