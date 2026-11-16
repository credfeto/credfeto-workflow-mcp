using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class TagViewResponseJsonTests : TestBase
{
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string LIGHTWEIGHT_JSON = "{\"sha\":\"" + SHA + "\",\"type\":\"lightweight\"}";

    private const string ANNOTATED_JSON = "{\"sha\":\"" + SHA + "\",\"type\":\"annotated\"}";

    [Fact]
    public static void ALightweightTagSerialisesInLowerCase()
    {
        Assert.Equal(expected: LIGHTWEIGHT_JSON, actual: Serialise(new(Sha: SHA, Type: TagType.Lightweight)));
    }

    [Fact]
    public static void AnAnnotatedTagSerialisesInLowerCase()
    {
        Assert.Equal(expected: ANNOTATED_JSON, actual: Serialise(new(Sha: SHA, Type: TagType.Annotated)));
    }

    [Theory]
    [InlineData(LIGHTWEIGHT_JSON)]
    [InlineData(ANNOTATED_JSON)]
    public static void AResponseRoundTrips(string json)
    {
        TagViewResponse? response = JsonSerializer.Deserialize(
            json: json,
            jsonTypeInfo: AppJsonContext.Default.TagViewResponse
        );

        Assert.NotNull(response);
        Assert.Equal(expected: json, actual: Serialise(response));
    }

    private static string Serialise(TagViewResponse response)
    {
        return JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.TagViewResponse);
    }
}
