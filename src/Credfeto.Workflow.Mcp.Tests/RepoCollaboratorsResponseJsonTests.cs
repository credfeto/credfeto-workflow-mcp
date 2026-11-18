using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class RepoCollaboratorsResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"collaborators\":[{\"login\":\"credfeto\",\"role\":\"admin\"},{\"login\":\"dependabot[bot]\",\"role\":\"-\"}]}";

    [Fact]
    public void AResponseSerialises()
    {
        RepoCollaboratorsResponse response = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Collaborators:
            [
                new RepoCollaborator(Login: "credfeto", Role: "admin"),
                new RepoCollaborator(Login: "dependabot[bot]", Role: "-"),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.RepoCollaboratorsResponse
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        RepoCollaboratorsResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.RepoCollaboratorsResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(
                value: response,
                jsonTypeInfo: AppJsonContext.Default.RepoCollaboratorsResponse
            )
        );
    }
}
