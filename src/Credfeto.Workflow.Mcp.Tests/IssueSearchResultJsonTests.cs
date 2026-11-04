using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueSearchResultJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"issues\":[{\"number\":19,\"state\":\"OPEN\",\"title\":\"issue search\",\"labels\":[\"feature\"]}],\"truncated\":true}";

    [Fact]
    public void AResultSerialisesWithItsTruncatedFlag()
    {
        IssueSearchResult result = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Issues: [new IssueSearchEntry(Number: 19, State: "OPEN", Title: "issue search", Labels: ["feature"])],
            Truncated: true
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.IssueSearchResult);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        IssueSearchResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.IssueSearchResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.IssueSearchResult)
        );
    }
}
