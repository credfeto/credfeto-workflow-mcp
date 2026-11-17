using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class ReleaseViewResultJsonTests : TestBase
{
    private const string TAG = "v1.2.3";
    private const string PUBLISHED_AT = "2026-11-17T09:39:14Z";
    private const string URL = "https://github.com/credfeto/credfeto-workflow-mcp/releases/tag/v1.2.3";
    private const string NAME = "Release 1.2.3";

    private const string NAMED_JSON =
        "{\"tag\":\""
        + TAG
        + "\",\"publishedAt\":\""
        + PUBLISHED_AT
        + "\",\"url\":\""
        + URL
        + "\",\"name\":\""
        + NAME
        + "\"}";

    private const string UNNAMED_JSON =
        "{\"tag\":\"" + TAG + "\",\"publishedAt\":\"" + PUBLISHED_AT + "\",\"url\":\"" + URL + "\",\"name\":\"\"}";

    [Fact]
    public static void AReleaseSerialisesInCamelCase()
    {
        Assert.Equal(
            expected: NAMED_JSON,
            actual: Serialise(new(Tag: TAG, PublishedAt: PUBLISHED_AT, Url: new(URL), Name: NAME))
        );
    }

    [Theory]
    [InlineData(NAMED_JSON)]
    [InlineData(UNNAMED_JSON)]
    public static void AResultRoundTrips(string json)
    {
        ReleaseViewResult? result = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.ReleaseViewResult
        );

        Assert.NotNull(result);
        Assert.Equal(expected: json, actual: Serialise(result));
    }

    private static string Serialise(ReleaseViewResult result)
    {
        return JsonSerializer.Serialize(value: result, jsonTypeInfo: AppJsonContext.Default.ReleaseViewResult);
    }
}
