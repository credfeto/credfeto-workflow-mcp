using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RepoSearchResultJsonTests : TestBase
{
    private const string JSON =
        "{\"owner\":\"credfeto\",\"repositories\":[{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"visibility\":\"PUBLIC\",\"status\":\"archived\",\"defaultBranch\":\"main\"}],\"truncated\":true}";

    [Fact]
    public void AResultSerialisesWithItsTruncatedFlag()
    {
        RepoSearchResult result = new(
            Owner: "credfeto",
            Repositories:
            [
                new RepoSearchEntry(
                    Repository: "credfeto/credfeto-workflow-mcp",
                    Visibility: "PUBLIC",
                    Status: "archived",
                    DefaultBranch: "main"
                ),
            ],
            Truncated: true
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RepoSearchResult);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        RepoSearchResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.RepoSearchResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.RepoSearchResult)
        );
    }
}
