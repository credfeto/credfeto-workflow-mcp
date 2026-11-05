using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class IssueListResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"issues\":[{\"number\":18,\"priority\":\"High\",\"blocked\":true,\"onHold\":false,\"assignees\":[\"credfeto\"],\"title\":\"issue list\",\"labels\":[\"High\",\"Blocked\"]},{\"number\":19,\"blocked\":false,\"onHold\":true,\"assignees\":[],\"title\":\"-\",\"labels\":[\"On Hold\"]}]}";

    [Fact]
    public void AResponseSerialisesWithoutAMissingPriority()
    {
        IssueListResponse response = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Issues:
            [
                new IssueSummary(
                    Number: 18,
                    Priority: "High",
                    Blocked: true,
                    OnHold: false,
                    Assignees: ["credfeto"],
                    Title: "issue list",
                    Labels: ["High", "Blocked"]
                ),
                new IssueSummary(
                    Number: 19,
                    Priority: null,
                    Blocked: false,
                    OnHold: true,
                    Assignees: [],
                    Title: "-",
                    Labels: ["On Hold"]
                ),
            ]
        );

        string json = JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.IssueListResponse);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void AResponseRoundTrips()
    {
        IssueListResponse? response = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.IssueListResponse
        );

        Assert.NotNull(response);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.IssueListResponse)
        );
    }
}
