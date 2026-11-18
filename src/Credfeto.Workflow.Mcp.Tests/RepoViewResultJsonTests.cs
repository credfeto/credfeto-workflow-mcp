using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RepoViewResultJsonTests : TestBase
{
    private const string JSON =
        "{\"defaultBranch\":\"main\",\"visibility\":\"PUBLIC\",\"allowMergeCommit\":true,\"allowSquashMerge\":false,\"allowRebaseMerge\":true,\"description\":\"A workflow MCP server\"}";

    private const string UNKNOWN_MERGE_METHODS_JSON =
        "{\"defaultBranch\":\"\",\"visibility\":\"PRIVATE\",\"description\":\"\"}";

    [Fact]
    public static void AResultSerialises()
    {
        RepoViewResult result = new(
            DefaultBranch: "main",
            Visibility: "PUBLIC",
            AllowMergeCommit: true,
            AllowSquashMerge: false,
            AllowRebaseMerge: true,
            Description: "A workflow MCP server"
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RepoViewResult);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public static void UnknownMergeMethodsAreLeftOut()
    {
        RepoViewResult result = new(
            DefaultBranch: string.Empty,
            Visibility: "PRIVATE",
            AllowMergeCommit: null,
            AllowSquashMerge: null,
            AllowRebaseMerge: null,
            Description: string.Empty
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RepoViewResult);

        Assert.Equal(expected: UNKNOWN_MERGE_METHODS_JSON, actual: json);
    }

    [Theory]
    [InlineData(JSON)]
    [InlineData(UNKNOWN_MERGE_METHODS_JSON)]
    public static void AResultRoundTrips(string json)
    {
        RepoViewResult? result = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.RepoViewResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RepoViewResult)
        );
    }
}
