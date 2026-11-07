using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrSearchResultJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"pullRequests\":[{\"number\":101,\"state\":\"MERGED\",\"headBranch\":\"-\",\"title\":\"search tools\"}],\"truncated\":true}";

    [Fact]
    public void AResultSerialisesWithItsTruncatedFlag()
    {
        PrSearchResult result = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            PullRequests: [new PrSearchEntry(Number: 101, State: "MERGED", HeadBranch: "-", Title: "search tools")],
            Truncated: true
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.PrSearchResult);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        PrSearchResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.PrSearchResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.PrSearchResult)
        );
    }
}
