using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowBoardItemsJsonTests : TestBase
{
    private const string JSON =
        "{\"repository\":\"credfeto/credfeto-workflow-mcp\",\"total\":2,\"items\":[{\"kind\":\"Issue\",\"repository\":\"credfeto/credfeto-workflow-mcp\",\"number\":1,\"state\":\"Approved\"},{\"kind\":\"PullRequest\",\"repository\":\"credfeto/other\",\"number\":2}]}";

    [Fact]
    public void BoardItemsSerialiseWithUnsetStatesLeftOut()
    {
        WorkflowBoardItems items = new(
            Repository: "credfeto/credfeto-workflow-mcp",
            Total: 2,
            Items:
            [
                new(
                    Kind: WorkflowBoardItemKind.Issue,
                    Repository: "credfeto/credfeto-workflow-mcp",
                    Number: 1,
                    State: "Approved"
                ),
                new(Kind: WorkflowBoardItemKind.PullRequest, Repository: "credfeto/other", Number: 2, State: null),
            ]
        );

        string json = JsonSerializer.Serialize(value: items, jsonTypeInfo: AppJsonContext.Default.WorkflowBoardItems);

        Assert.Equal(expected: JSON, actual: json);
    }

    [Fact]
    public void BoardItemsRoundTrip()
    {
        WorkflowBoardItems? items = JsonSerializer.Deserialize(
            json: JSON,
            jsonTypeInfo: AppJsonContext.Default.WorkflowBoardItems
        );

        Assert.NotNull(items);
        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: items, jsonTypeInfo: AppJsonContext.Default.WorkflowBoardItems)
        );
    }
}
