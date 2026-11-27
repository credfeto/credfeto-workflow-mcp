using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CommitCheckRunsResponseJsonTests : TestBase
{
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string JSON =
        "{\"repo\":\"credfeto/credfeto-workflow-mcp\",\"sha\":\""
        + SHA
        + "\",\"running\":1,\"checkRuns\":["
        + "{\"id\":1,\"name\":\"build\",\"status\":\"in_progress\",\"appId\":15368},"
        + "{\"id\":2,\"name\":\"lint\",\"status\":\"completed\",\"conclusion\":\"success\"}]}";

    [Fact]
    public static void AResponseSerialisesWithoutTheConclusionOfAnUnfinishedRun()
    {
        CommitCheckRunsResponse response = new(
            Repo: "credfeto/credfeto-workflow-mcp",
            Sha: SHA,
            Running: 1,
            CheckRuns:
            [
                new(Id: 1, Name: "build", Status: "in_progress", Conclusion: null, AppId: 15368),
                new(Id: 2, Name: "lint", Status: "completed", Conclusion: "success", AppId: null),
            ]
        );

        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(
                value: response,
                jsonTypeInfo: AppJsonContext.Default.CommitCheckRunsResponse
            )
        );
    }
}
