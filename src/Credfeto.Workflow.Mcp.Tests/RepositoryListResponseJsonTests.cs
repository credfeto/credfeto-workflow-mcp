using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RepositoryListResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"owner\":\"credfeto\",\"repositories\":[{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"visibility\":\"PUBLIC\",\"archived\":false,\"defaultBranch\":\"main\"},{\"repository\":\"credfeto/old\",\"visibility\":\"PRIVATE\",\"archived\":true,\"defaultBranch\":\"-\"}]}";

    [Fact]
    public void AResponseSerialises()
    {
        RepositoryListResponse response = new(
            Owner: "credfeto",
            Repositories:
            [
                new RepositoryEntry(
                    Repository: "credfeto/credfeto-workflow-mcp",
                    Visibility: "PUBLIC",
                    Archived: false,
                    DefaultBranch: "main"
                ),
                new RepositoryEntry(
                    Repository: "credfeto/old",
                    Visibility: "PRIVATE",
                    Archived: true,
                    DefaultBranch: "-"
                ),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.RepositoryListResponse
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        RepositoryListResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.RepositoryListResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(
                value: response,
                jsonTypeInfo: AppJsonContext.Default.RepositoryListResponse
            )
        );
    }
}
