using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CodeSearchResultJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"files\":[{\"path\":\"src/Program.cs\",\"matches\":3}],\"truncated\":true}";

    [Fact]
    public void AResultSerialisesWithItsTruncatedFlag()
    {
        CodeSearchResult result = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Files: [new CodeSearchEntry(Path: "src/Program.cs", Matches: 3)],
            Truncated: true
        );

        string json = JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.CodeSearchResult);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResultRoundTrips()
    {
        CodeSearchResult? result = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.CodeSearchResult
        );

        Assert.NotNull(result);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.CodeSearchResult)
        );
    }
}
