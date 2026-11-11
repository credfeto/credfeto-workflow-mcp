using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowListResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"workflows\":[{\"name\":\"Build\",\"path\":\".github/workflows/build.yml\",\"state\":\"active\"},{\"name\":\"-\",\"path\":\".github/workflows/old.yml\",\"state\":\"disabled_manually\"}]}";

    [Fact]
    public void AResponseSerialises()
    {
        WorkflowListResponse response = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Workflows:
            [
                new WorkflowEntry(Name: "Build", Path: ".github/workflows/build.yml", State: "active"),
                new WorkflowEntry(Name: "-", Path: ".github/workflows/old.yml", State: "disabled_manually"),
            ]
        );

        string json = JsonSerializer.Serialize(
            value: response,
            jsonTypeInfo: AppJsonContext.Default.WorkflowListResponse
        );

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        WorkflowListResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.WorkflowListResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.WorkflowListResponse)
        );
    }
}
