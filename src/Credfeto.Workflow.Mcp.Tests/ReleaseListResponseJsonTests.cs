using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class ReleaseListResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"releases\":[{\"tag\":\"v1.9.1\",\"publishedAt\":\"2026-11-17T10:00:00Z\",\"isLatest\":false,\"isPrerelease\":false,\"name\":\"-\"},{\"tag\":\"v2.0.0\",\"publishedAt\":\"2026-11-16T10:00:00Z\",\"isLatest\":true,\"isPrerelease\":true,\"name\":\"Release 2.0.0\"}]}";

    [Fact]
    public void AResponseSerialises()
    {
        ReleaseListResponse response = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Releases:
            [
                new ReleaseSummary(
                    Tag: "v1.9.1",
                    PublishedAt: "2026-11-17T10:00:00Z",
                    IsLatest: false,
                    IsPrerelease: false,
                    Name: "-"
                ),
                new ReleaseSummary(
                    Tag: "v2.0.0",
                    PublishedAt: "2026-11-16T10:00:00Z",
                    IsLatest: true,
                    IsPrerelease: true,
                    Name: "Release 2.0.0"
                ),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.ReleaseListResponse
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        ReleaseListResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.ReleaseListResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.ReleaseListResponse)
        );
    }
}
